package main

import "strings"

// modelPricing holds a model's USD rates per million tokens. Cache traffic is
// billed as a multiple of the input rate rather than at its own posted rate:
// a cache write costs 1.25x input on the 5-minute TTL and 2x input on the
// 1-hour TTL, and a cache read costs 0.1x input.
type modelPricing struct {
	Input  float64
	Output float64
}

func (p modelPricing) cacheWrite5m() float64 { return p.Input * 1.25 }
func (p modelPricing) cacheWrite1h() float64 { return p.Input * 2.0 }
func (p modelPricing) cacheRead() float64    { return p.Input * 0.1 }

// legacyOpusIDs are the Opus versions that predate the 4.5 price drop and
// still bill at $15/$75. Matching is by ID substring, so these are listed
// explicitly: "opus-4-1" would not catch Opus 4.0, whose ID is
// claude-opus-4-20250514, and a looser "opus-4" pattern would wrongly catch
// Opus 4.5 and later.
var legacyOpusIDs = []string{
	"claude-3-opus",
	"claude-opus-4-0",
	"claude-opus-4-20",
	"claude-opus-4-1",
}

// pricingFor returns a model's rates and whether we know them. A model routed
// through a non-Anthropic provider has no Claude rate; callers show its cost
// as unknown rather than pricing it as though it were Claude.
func pricingFor(modelID string) (modelPricing, bool) {
	id := strings.ToLower(modelID)

	for _, legacy := range legacyOpusIDs {
		if strings.Contains(id, legacy) {
			return modelPricing{Input: 15, Output: 75}, true
		}
	}

	switch {
	case strings.Contains(id, "fable"), strings.Contains(id, "mythos"):
		return modelPricing{Input: 10, Output: 50}, true
	case strings.Contains(id, "opus"):
		return modelPricing{Input: 5, Output: 25}, true
	// Sonnet dropped from $3/$15 to $2/$10 with Sonnet 5.
	case strings.Contains(id, "sonnet-5"):
		return modelPricing{Input: 2, Output: 10}, true
	case strings.Contains(id, "sonnet"):
		return modelPricing{Input: 3, Output: 15}, true
	case strings.Contains(id, "haiku"):
		return modelPricing{Input: 1, Output: 5}, true
	}

	return modelPricing{}, false
}

// modelCost prices one model's token counts. The bool reports whether the
// model has a known rate; when it is false the cost is 0 and the caller shows
// a dash instead of a number that would look authoritative.
func modelCost(modelID string, u ModelUsage) (float64, bool) {
	p, ok := pricingFor(modelID)
	if !ok {
		return 0, false
	}

	const perMillion = 1_000_000.0
	total := float64(u.InputTokens)*p.Input +
		float64(u.OutputTokens)*p.Output +
		float64(u.CacheCreation5mTokens)*p.cacheWrite5m() +
		float64(u.CacheCreation1hTokens)*p.cacheWrite1h() +
		float64(u.CacheReadInputTokens)*p.cacheRead()

	return total / perMillion, true
}
