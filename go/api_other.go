//go:build !darwin

package main

import "fmt"

// readClaudeCookies returns an error on non-macOS platforms.
func readClaudeCookies() (sessionKey, orgID string, err error) {
	return "", "", fmt.Errorf("Claude desktop cookie access is only supported on macOS")
}
