//go:build darwin

package main

import (
	"bytes"
	"crypto/aes"
	"crypto/cipher"
	"crypto/sha1"
	"database/sql"
	"fmt"
	"os"
	"os/exec"
	"regexp"

	"golang.org/x/crypto/pbkdf2"
	_ "modernc.org/sqlite"
)

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

	var scanErr, decryptErr error
	for rows.Next() {
		var name string
		var encVal []byte
		if err := rows.Scan(&name, &encVal); err != nil {
			if scanErr == nil {
				scanErr = fmt.Errorf("scanning row: %w", err)
			}
			continue
		}
		val, err := decryptChromeV10Cookie(encVal, aesKey)
		if err != nil {
			if decryptErr == nil {
				decryptErr = fmt.Errorf("decrypting cookie %s: %w", name, err)
			}
			continue
		}
		switch name {
		case "sessionKey":
			sessionKey = val
		case "lastActiveOrg":
			orgID = val
		}
	}

	if err := rows.Err(); err != nil {
		return "", "", fmt.Errorf("iterating cookie rows: %w", err)
	}

	if sessionKey == "" {
		if scanErr != nil {
			return "", "", fmt.Errorf("session key not found (%w) — log in via the Claude desktop app", scanErr)
		}
		if decryptErr != nil {
			return "", "", fmt.Errorf("session key not found (%w) — log in via the Claude desktop app", decryptErr)
		}
		return "", "", fmt.Errorf("session key not found — log in via the Claude desktop app")
	}
	if orgID == "" {
		if scanErr != nil {
			return "", "", fmt.Errorf("organization ID not found (%w)", scanErr)
		}
		if decryptErr != nil {
			return "", "", fmt.Errorf("organization ID not found (%w)", decryptErr)
		}
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
