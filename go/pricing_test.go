package main

import (
	"math"
	"testing"
)

func TestPricingForKnownModels(t *testing.T) {
	tests := []struct {
		modelID string
		input   float64
		output  float64
	}{
		{"claude-opus-5", 5, 25},
		{"claude-opus-4-8", 5, 25},
		{"claude-opus-4-7", 5, 25},
		{"claude-opus-4-6", 5, 25},
		{"claude-opus-4-5", 5, 25},
		{"claude-fable-5", 10, 50},
		{"claude-mythos-5", 10, 50},
		{"claude-haiku-4-5-20251001", 1, 5},

		// Sonnet 5 is cheaper than Sonnet 4.6 — pricing it at the older
		// family rate overstates its cost by half.
		{"claude-sonnet-5", 2, 10},
		{"claude-sonnet-4-6", 3, 15},
		{"claude-sonnet-4-5-20250929", 3, 15},

		// Opus billed $15/$75 before the 4.5 price drop.
		{"claude-3-opus-20240229", 15, 75},
		{"claude-opus-4-20250514", 15, 75},
		{"claude-opus-4-1-20250805", 15, 75},
	}

	for _, tc := range tests {
		p, ok := pricingFor(tc.modelID)
		if !ok {
			t.Errorf("pricingFor(%q): no rate found", tc.modelID)
			continue
		}
		if p.Input != tc.input || p.Output != tc.output {
			t.Errorf("pricingFor(%q) = $%g/$%g, want $%g/$%g",
				tc.modelID, p.Input, p.Output, tc.input, tc.output)
		}
	}
}

func TestPricingForUnknownModel(t *testing.T) {
	// A model routed through a non-Anthropic provider must not be priced as
	// though it were Claude.
	for _, id := range []string{"gpt-4o", "llama-3.1-70b", ""} {
		if _, ok := pricingFor(id); ok {
			t.Errorf("pricingFor(%q): expected no rate, got one", id)
		}
	}
}

func TestCacheMultipliers(t *testing.T) {
	p, _ := pricingFor("claude-opus-5")
	if got, want := p.cacheWrite5m(), 6.25; got != want {
		t.Errorf("cacheWrite5m() = %g, want %g", got, want)
	}
	if got, want := p.cacheWrite1h(), 10.0; got != want {
		t.Errorf("cacheWrite1h() = %g, want %g", got, want)
	}
	if got, want := p.cacheRead(), 0.5; got != want {
		t.Errorf("cacheRead() = %g, want %g", got, want)
	}
}

func TestModelCost(t *testing.T) {
	// One million of each token type on Opus 5, so each term reads as its
	// own rate: 5 + 25 + 6.25 + 10 + 0.5.
	u := ModelUsage{
		InputTokens:           1_000_000,
		OutputTokens:          1_000_000,
		CacheCreation5mTokens: 1_000_000,
		CacheCreation1hTokens: 1_000_000,
		CacheReadInputTokens:  1_000_000,
	}

	cost, ok := modelCost("claude-opus-5", u)
	if !ok {
		t.Fatal("modelCost: expected a known rate for claude-opus-5")
	}
	if want := 46.75; math.Abs(cost-want) > 1e-9 {
		t.Errorf("modelCost = %g, want %g", cost, want)
	}
}

func TestModelCostSeparatesCacheTTLs(t *testing.T) {
	// The same cache-write volume costs more on the 1-hour TTL. Collapsing
	// the two into one 1.25x bucket, as a single cache_creation rate would,
	// understates the 1-hour case.
	write5m := ModelUsage{CacheCreation5mTokens: 1_000_000}
	write1h := ModelUsage{CacheCreation1hTokens: 1_000_000}

	cost5m, _ := modelCost("claude-opus-5", write5m)
	cost1h, _ := modelCost("claude-opus-5", write1h)

	if cost5m != 6.25 {
		t.Errorf("5-minute cache write = %g, want 6.25", cost5m)
	}
	if cost1h != 10.0 {
		t.Errorf("1-hour cache write = %g, want 10.0", cost1h)
	}
}

func TestModelCostUnknownModelIsZero(t *testing.T) {
	u := ModelUsage{InputTokens: 1_000_000, OutputTokens: 1_000_000}
	cost, ok := modelCost("some-other-provider/model", u)
	if ok {
		t.Error("modelCost: expected unknown rate")
	}
	if cost != 0 {
		t.Errorf("modelCost = %g, want 0 for an unpriced model", cost)
	}
}
