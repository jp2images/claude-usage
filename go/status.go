package main

import (
	"encoding/json"
	"image/color"
	"net/http"
	"time"
)

type serviceStatus struct {
	Indicator   string
	Description string
}

var statusHTTPClient = &http.Client{Timeout: 5 * time.Second}

func fetchServiceStatus() serviceStatus {
	resp, err := statusHTTPClient.Get("https://status.claude.com/api/v2/status.json")
	if err != nil {
		return serviceStatus{Indicator: "unknown", Description: "Status unavailable"}
	}
	defer resp.Body.Close()

	var payload struct {
		Status struct {
			Indicator   string `json:"indicator"`
			Description string `json:"description"`
		} `json:"status"`
	}
	if err := json.NewDecoder(resp.Body).Decode(&payload); err != nil {
		return serviceStatus{Indicator: "unknown", Description: "Status unavailable"}
	}

	return serviceStatus{
		Indicator:   payload.Status.Indicator,
		Description: payload.Status.Description,
	}
}

// statusColor maps a Statuspage indicator to a traffic-light color.
// Indicator values: "none" (all good), "minor", "major", "critical".
func statusColor(indicator string) color.Color {
	switch indicator {
	case "none":
		return color.RGBA{R: 40, G: 167, B: 69, A: 255}
	case "minor":
		return color.RGBA{R: 255, G: 193, B: 7, A: 255}
	case "major":
		return color.RGBA{R: 253, G: 126, B: 20, A: 255}
	case "critical":
		return color.RGBA{R: 220, G: 53, B: 69, A: 255}
	default:
		return color.RGBA{R: 108, G: 117, B: 125, A: 255}
	}
}
