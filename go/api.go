package main

import (
	"encoding/json"
	"fmt"
	"io"
	"net/http"
	"time"
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
