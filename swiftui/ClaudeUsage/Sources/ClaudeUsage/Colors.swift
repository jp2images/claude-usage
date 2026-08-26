import SwiftUI

/// Colours shared by the history window. The model-family hexes are the same
/// ones scripts/chart-usage.cs uses, so the app and the trend chart agree on
/// what each family looks like.
enum Palette {
    /// #0A84FF. Doubles as Sonnet's family colour.
    static let accent = Color(red: 10/255, green: 132/255, blue: 255/255)

    private static let fable = Color(red: 232/255, green: 131/255, blue: 58/255)   // #E8833A
    private static let opus = Color(red: 180/255, green: 90/255, blue: 214/255)    // #B45AD6
    private static let sonnet = accent                                             // #0A84FF
    private static let haiku = Color(red: 40/255, green: 167/255, blue: 69/255)    // #28A745
    private static let other = Color(red: 136/255, green: 136/255, blue: 136/255)  // #888888

    /// Semi-transparent rather than a fixed light fill, so the stripe reads
    /// against both the light and the dark window background.
    static let rowStripe = Color.gray.opacity(0.06)
    static let totalsTint = accent.opacity(0.08)

    /// Stripes every other row, starting with the first.
    static func stripe(row index: Int) -> Color {
        index.isMultiple(of: 2) ? rowStripe : .clear
    }

    /// Family colour for a model ID. Matched in the same order as the chart
    /// script's family list, and Mythos takes Fable's colour there too.
    static func model(for modelID: String) -> Color {
        let id = modelID.lowercased()
        let table: [(String, Color)] = [
            ("fable", fable), ("mythos", fable),
            ("opus", opus), ("sonnet", sonnet), ("haiku", haiku),
        ]
        for (needle, color) in table where id.contains(needle) { return color }
        return other
    }
}
