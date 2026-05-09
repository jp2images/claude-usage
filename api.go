package main

import (
	"bytes"
	"crypto/aes"
	"crypto/cipher"
	"crypto/sha1"
	"database/sql"
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"os"
	"os/exec"
	"regexp"
	"time"

	"golang.org/x/crypto/pbkdf2"
	_ "modernc.org/sqlite"
)

// UsagePeriod is a single usage metric with utilization percentage and reset time.
type UsagePeriod struct {
	Utilization float64 `json:"utilization"`
	ResetsAt    *string `json:"resets_at"`
}

// ExtraUsage represents the optional purchased extra-credit balance.
type ExtraUsage struct {
	IsEnabled    bool     `json:"is_enabled"`
	MonthlyLimit *float64 `json:"monthly_limit"`
	UsedCredits  *float64 `json:"used_credits"`
	Utilization  *float64 `json:"utilization"`
	Currency     *string  `json:"currency"`
}

// PlanUsage is the response from /api/organizations/{id}/usage.
type PlanUsage struct {
	FiveHour          *UsagePeriod `json:"five_hour"`
	SevenDay          *UsagePeriod `json:"seven_day"`
	SevenDayOAuthApps *UsagePeriod `json:"seven_day_oauth_apps"`
	SevenDayOpus      *UsagePeriod `json:"seven_day_opus"`
	SevenDaySonnet    *UsagePeriod `json:"seven_day_sonnet"`
	SevenDayCowork    *UsagePeriod `json:"seven_day_cowork"`
	SevenDayOmelette  *UsagePeriod `json:"seven_day_omelette"` // Claude Design
	ExtraUsage        ExtraUsage   `json:"extra_usage"`
}

// RateLimits is the response from /api/organizations/{id}/rate_limits.
type RateLimits struct {
	RateLimitTier string `json:"rate_limit_tier"`
}

// loadPlanUsage fetches live plan usage from the Claude.ai API using the
// Claude desktop app's encrypted session cookies.
func loadPlanUsage() (*PlanUsage, *RateLimits, error) {
	sessionKey, orgID, err := readClaudeCookies()
	if err != nil {
		return nil, nil, fmt.Errorf("reading cookies: %w", err)
	}

	usage, err := fetchClaudeAPI[PlanUsage](
		fmt.Sprintf("https://claude.ai/api/organizations/%s/usage", orgID),
		sessionKey,
	)
	if err != nil {
		return nil, nil, fmt.Errorf("fetching usage: %w", err)
	}

	limits, err := fetchClaudeAPI[RateLimits](
		fmt.Sprintf("https://claude.ai/api/organizations/%s/rate_limits", orgID),
		sessionKey,
	)
	if err != nil {
		return nil, nil, fmt.Errorf("fetching rate limits: %w", err)
	}

	return usage, limits, nil
}

func fetchClaudeAPI[T any](url, sessionKey string) (*T, error) {
	req, err := http.NewRequest("GET", url, nil)
	if err != nil {
		return nil, err
	}
	req.Header.Set("Cookie", "sessionKey="+sessionKey)
	req.Header.Set("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36")
	req.Header.Set("Accept", "application/json")

	client := &http.Client{Timeout: 15 * time.Second}
	resp, err := client.Do(req)
	if err != nil {
		return nil, err
	}
	defer resp.Body.Close()

	if resp.StatusCode == 401 || resp.StatusCode == 403 {
		return nil, fmt.Errorf("session expired — open the Claude desktop app to refresh")
	}
	if resp.StatusCode != 200 {
		return nil, fmt.Errorf("HTTP %d", resp.StatusCode)
	}

	body, err := io.ReadAll(resp.Body)
	if err != nil {
		return nil, err
	}

	var result T
	if err := json.Unmarshal(body, &result); err != nil {
		return nil, fmt.Errorf("parsing response: %w", err)
	}

	return &result, nil
}

// readClaudeCookies decrypts the sessionKey and lastActiveOrg cookies from the
// Claude desktop app's Electron/Chromium cookie store on macOS.
func readClaudeCookies() (sessionKey, orgID string, err error) {
	out, err := exec.Command(
		"security", "find-generic-password",
		"-s", "Claude Safe Storage",
		"-a", "Claude Key",
		"-w",
	).Output()
	if err != nil {
		return "", "", fmt.Errorf("keychain lookup failed (is Claude desktop installed?): %w", err)
	}
	password := bytes.TrimSpace(out)
	aesKey := pbkdf2.Key(password, []byte("saltysalt"), 1003, 16, sha1.New)

	home, err := os.UserHomeDir()
	if err != nil {
		return "", "", err
	}
	cookiePath := home + "/Library/Application Support/Claude/Cookies"
	db, err := sql.Open("sqlite", "file:"+cookiePath+"?mode=ro&immutable=1")
	if err != nil {
		return "", "", fmt.Errorf("opening cookie database: %w", err)
	}
	defer db.Close()

	rows, err := db.Query(`
		SELECT name, encrypted_value
		FROM cookies
		WHERE host_key LIKE '%claude.ai%'
		  AND name IN ('sessionKey', 'lastActiveOrg')
	`)
	if err != nil {
		return "", "", fmt.Errorf("querying cookies: %w", err)
	}
	defer rows.Close()

	for rows.Next() {
		var name string
		var encVal []byte
		if err := rows.Scan(&name, &encVal); err != nil {
			continue
		}
		val, err := decryptChromeV10Cookie(encVal, aesKey)
		if err != nil {
			continue
		}
		switch name {
		case "sessionKey":
			sessionKey = val
		case "lastActiveOrg":
			orgID = val
		}
	}

	if sessionKey == "" {
		return "", "", fmt.Errorf("session key not found — log in via the Claude desktop app")
	}
	if orgID == "" {
		return "", "", fmt.Errorf("organization ID not found in cookies")
	}
	return sessionKey, orgID, nil
}

// decryptChromeV10Cookie decrypts a v10-prefixed AES-CBC cookie value using
// the Chromium/Electron macOS encryption scheme.
func decryptChromeV10Cookie(encVal []byte, key []byte) (string, error) {
	if len(encVal) < 4 || string(encVal[:3]) != "v10" {
		return string(encVal), nil // unencrypted
	}

	ciphertext := encVal[3:]
	if len(ciphertext) == 0 || len(ciphertext)%aes.BlockSize != 0 {
		return "", fmt.Errorf("invalid ciphertext length")
	}

	block, err := aes.NewCipher(key)
	if err != nil {
		return "", err
	}

	iv := bytes.Repeat([]byte(" "), aes.BlockSize)
	plaintext := make([]byte, len(ciphertext))
	cipher.NewCBCDecrypter(block, iv).CryptBlocks(plaintext, ciphertext)

	// Remove PKCS7 padding
	padLen := int(plaintext[len(plaintext)-1])
	if padLen == 0 || padLen > aes.BlockSize || padLen > len(plaintext) {
		return "", fmt.Errorf("invalid PKCS7 padding")
	}
	plaintext = plaintext[:len(plaintext)-padLen]

	// The first 16 bytes may be a nonce prefix — extract by pattern matching.
	if idx := bytes.Index(plaintext, []byte("sk-ant-")); idx >= 0 {
		return string(plaintext[idx:]), nil
	}
	uuidRe := regexp.MustCompile(`[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}`)
	if m := uuidRe.Find(plaintext); m != nil {
		return string(m), nil
	}

	return string(bytes.TrimRight(plaintext, "\x00")), nil
}
