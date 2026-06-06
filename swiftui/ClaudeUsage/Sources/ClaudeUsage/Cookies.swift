import Foundation
import CommonCrypto
import SQLite3

/// Reads the Claude desktop app's sessionKey + lastActiveOrg cookies from its
/// Electron/Chromium cookie store on macOS. Swift port of go/api_darwin.go:
/// Keychain ("Claude Safe Storage") -> PBKDF2 (SHA-1, 1003 iters, "saltysalt")
/// -> 16-byte AES-128 key -> AES-CBC cookies with a fixed 16-space IV.
enum ClaudeCookies {

    struct Cookies: Sendable {
        let sessionKey: String
        let orgID: String
    }

    static func read() throws -> Cookies {
        let key = try deriveKey(from: keychainPassword())

        let home = FileManager.default.homeDirectoryForCurrentUser
        let base = home.appendingPathComponent("Library/Application Support/Claude")
        var cookieURL = base.appendingPathComponent("Cookies")
        if !FileManager.default.fileExists(atPath: cookieURL.path) {
            cookieURL = base.appendingPathComponent("Network/Cookies")
        }
        guard FileManager.default.fileExists(atPath: cookieURL.path) else {
            throw ClaudeUsageError("Claude cookie store not found — is the Claude desktop app installed and logged in?")
        }

        var sessionKey: String?
        var orgID: String?
        for (name, encrypted) in try readRows(cookieURL: cookieURL) {
            let value = try decryptCookie(encrypted, key: key)
            switch name {
            case "sessionKey": sessionKey = value
            case "lastActiveOrg": orgID = value
            default: break
            }
        }

        guard let sessionKey, !sessionKey.isEmpty else {
            throw ClaudeUsageError("session key not found — log in via the Claude desktop app")
        }
        guard let orgID, !orgID.isEmpty else {
            throw ClaudeUsageError("organization ID not found in cookies")
        }
        return Cookies(sessionKey: sessionKey, orgID: orgID)
    }

    // MARK: Keychain

    private static func keychainPassword() throws -> String {
        let process = Process()
        process.executableURL = URL(fileURLWithPath: "/usr/bin/security")
        process.arguments = ["find-generic-password", "-s", "Claude Safe Storage", "-a", "Claude Key", "-w"]
        let out = Pipe()
        process.standardOutput = out
        process.standardError = Pipe()

        try process.run()
        process.waitUntilExit()

        let data = out.fileHandleForReading.readDataToEndOfFile()
        guard process.terminationStatus == 0,
              let password = String(data: data, encoding: .utf8)?.trimmingCharacters(in: .whitespacesAndNewlines),
              !password.isEmpty
        else {
            throw ClaudeUsageError("keychain lookup failed (is Claude desktop installed?)")
        }
        return password
    }

    // MARK: Key derivation & decryption

    private static func deriveKey(from password: String) throws -> Data {
        let keyLength = 16
        var derived = Data(count: keyLength)
        let salt = Array("saltysalt".utf8)

        let status = derived.withUnsafeMutableBytes { (derivedPtr: UnsafeMutableRawBufferPointer) -> Int32 in
            salt.withUnsafeBufferPointer { saltPtr in
                password.withCString { pwPtr in
                    CCKeyDerivationPBKDF(
                        CCPBKDFAlgorithm(kCCPBKDF2),
                        pwPtr, strlen(pwPtr),
                        saltPtr.baseAddress, salt.count,
                        CCPseudoRandomAlgorithm(kCCPRFHmacAlgSHA1),
                        1003,
                        derivedPtr.bindMemory(to: UInt8.self).baseAddress, keyLength
                    )
                }
            }
        }
        guard status == kCCSuccess else {
            throw ClaudeUsageError("key derivation failed")
        }
        return derived
    }

    private static func decryptCookie(_ encrypted: Data, key: Data) throws -> String {
        // Values are prefixed "v10"; anything else is treated as plaintext.
        guard encrypted.count > 3, encrypted.prefix(3) == Data("v10".utf8) else {
            return String(decoding: encrypted, as: UTF8.self)
        }

        let ciphertext = encrypted.dropFirst(3)
        guard !ciphertext.isEmpty, ciphertext.count % kCCBlockSizeAES128 == 0 else {
            throw ClaudeUsageError("invalid ciphertext length")
        }

        let iv = Data(repeating: 0x20, count: kCCBlockSizeAES128) // 16 spaces
        let inputData = Data(ciphertext)
        let outputCapacity = inputData.count + kCCBlockSizeAES128
        var output = Data(count: outputCapacity)
        var written = 0

        let status = output.withUnsafeMutableBytes { outPtr in
            inputData.withUnsafeBytes { inPtr in
                key.withUnsafeBytes { keyPtr in
                    iv.withUnsafeBytes { ivPtr in
                        CCCrypt(
                            CCOperation(kCCDecrypt),
                            CCAlgorithm(kCCAlgorithmAES),
                            CCOptions(kCCOptionPKCS7Padding),
                            keyPtr.baseAddress, key.count,
                            ivPtr.baseAddress,
                            inPtr.baseAddress, inputData.count,
                            outPtr.baseAddress, outputCapacity,
                            &written
                        )
                    }
                }
            }
        }
        guard status == kCCSuccess else {
            throw ClaudeUsageError("cookie decryption failed")
        }
        output.removeSubrange(written..<output.count)

        let text = String(decoding: output, as: UTF8.self)
        // Some builds prefix a nonce; recover the value by pattern, as the Go code does.
        if let range = text.range(of: "sk-ant-") {
            return String(text[range.lowerBound...])
        }
        if let match = text.range(of: #"[a-f0-9]{8}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{4}-[a-f0-9]{12}"#,
                                  options: .regularExpression) {
            return String(text[match])
        }
        return text.trimmingCharacters(in: CharacterSet(charactersIn: "\0"))
    }

    // MARK: SQLite

    private static func readRows(cookieURL: URL) throws -> [(name: String, value: Data)] {
        // Copy the DB so we can read while the desktop app holds a lock.
        let temp = FileManager.default.temporaryDirectory
            .appendingPathComponent("claude-cookies-\(UUID().uuidString).db")
        try FileManager.default.copyItem(at: cookieURL, to: temp)
        defer { try? FileManager.default.removeItem(at: temp) }

        var db: OpaquePointer?
        guard sqlite3_open_v2(temp.path, &db, SQLITE_OPEN_READONLY, nil) == SQLITE_OK else {
            throw ClaudeUsageError("opening cookie database failed")
        }
        defer { sqlite3_close(db) }

        let sql = """
            SELECT name, encrypted_value FROM cookies
            WHERE host_key LIKE '%claude.ai%'
              AND name IN ('sessionKey', 'lastActiveOrg')
            """
        var stmt: OpaquePointer?
        guard sqlite3_prepare_v2(db, sql, -1, &stmt, nil) == SQLITE_OK else {
            throw ClaudeUsageError("querying cookies failed")
        }
        defer { sqlite3_finalize(stmt) }

        var rows: [(String, Data)] = []
        while sqlite3_step(stmt) == SQLITE_ROW {
            guard let cName = sqlite3_column_text(stmt, 0) else { continue }
            let name = String(cString: cName)
            if let blob = sqlite3_column_blob(stmt, 1) {
                let count = Int(sqlite3_column_bytes(stmt, 1))
                rows.append((name, Data(bytes: blob, count: count)))
            }
        }
        return rows
    }
}
