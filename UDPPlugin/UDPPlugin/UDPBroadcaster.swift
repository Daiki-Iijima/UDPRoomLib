import Foundation
import UIKit

@objcMembers
public class UDPBroadcaster: NSObject {
    public static let shared = UDPBroadcaster()
    private override init() {}

    private var socketFD: Int32 = -1
    private var receiveSocketFD: Int32 = -1
    private var broadcastAddress: String = "255.255.255.255"
    private var port: UInt16 = 5000

    // バッファ設定
    private var messageBuffer: [String] = []
    private let bufferLimit = 100

    // MARK: - ログ
    enum LogLevel: String {
        case info = "INFO"
        case warn = "WARN"
        case error = "ERROR"
    }

    private func log(_ message: String, level: LogLevel = .info) {
        print("[\(level.rawValue)] UDPBroadcaster: \(message)")
    }

    private func formatPacket(_ raw: String) -> String {
        let dict: [String: Any] = [
            "timestamp": ISO8601DateFormatter().string(from: Date()),
            "payload": raw,
        ]
        if let jsonData = try? JSONSerialization.data(withJSONObject: dict),
           let jsonString = String(data: jsonData, encoding: .utf8) {
            return jsonString
        } else {
            return raw
        }
    }

    // MARK: - バッファ操作
    private func bufferMessage(_ message: String) {
        objc_sync_enter(self)
        if messageBuffer.count >= bufferLimit {
            messageBuffer.removeFirst()
        }
        messageBuffer.append(message)
        objc_sync_exit(self)
    }

    @objc public func getNextBufferedMessage() -> String? {
        objc_sync_enter(self)
        defer { objc_sync_exit(self) }

        guard !messageBuffer.isEmpty else { return nil }
        return messageBuffer.removeFirst()
    }

    // MARK: - 送信
    @objc public func startBroadcasting(port: UInt16) {
        self.port = port
        socketFD = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard socketFD >= 0 else {
            log("ソケット作成失敗", level: .error)
            return
        }

        var enable: Int32 = 1
        setsockopt(socketFD, SOL_SOCKET, SO_BROADCAST, &enable, socklen_t(MemoryLayout.size(ofValue: enable)))

        if let ip = getIPAddress() {
            broadcastAddress = calculateBroadcastAddress(ip: ip, subnetMask: "255.255.255.0")
            log("検出されたIP: \(ip), ブロードキャスト先: \(broadcastAddress)")
        } else {
            log("IP取得失敗、255.255.255.255で送信します", level: .warn)
        }

        log("UDPソケット作成（ポート: \(port)）")
    }

    @objc public func send(message: String) {
        guard socketFD >= 0 else { return }

        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = in_port_t(port).bigEndian
        addr.sin_addr.s_addr = inet_addr(broadcastAddress)

        let formatted = formatPacket(message)
        guard let data = formatted.data(using: .utf8) else {
            log("メッセージのエンコード失敗", level: .error)
            return
        }

        data.withUnsafeBytes { buffer in
            let ptr = buffer.baseAddress!
            let result = withUnsafePointer(to: &addr) {
                $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                    sendto(socketFD, ptr, data.count, 0, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
                }
            }

            if result < 0 {
                perror("sendto failed")
            } else {
                log("送信成功（バイト数: \(result)）")
            }
        }
    }

    // MARK: - 受信
    @objc public func startReceiving(port: UInt16) {
        self.port = port
        receiveSocketFD = socket(AF_INET, SOCK_DGRAM, IPPROTO_UDP)
        guard receiveSocketFD >= 0 else {
            log("受信用ソケット作成失敗", level: .error)
            return
        }

        var addr = sockaddr_in()
        addr.sin_family = sa_family_t(AF_INET)
        addr.sin_port = in_port_t(port).bigEndian
        addr.sin_addr.s_addr = INADDR_ANY

        var reuse: Int32 = 1
        setsockopt(receiveSocketFD, SOL_SOCKET, SO_REUSEADDR, &reuse, socklen_t(MemoryLayout.size(ofValue: reuse)))

        let bindResult = withUnsafePointer(to: &addr) {
            $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                bind(receiveSocketFD, $0, socklen_t(MemoryLayout<sockaddr_in>.size))
            }
        }

        if bindResult < 0 {
            perror("bind failed")
            close(receiveSocketFD)
            receiveSocketFD = -1
            return
        }

        DispatchQueue.global(qos: .background).async {
            var buffer = [UInt8](repeating: 0, count: 1024)
            var senderAddr = sockaddr_in()
            var addrLen = socklen_t(MemoryLayout<sockaddr_in>.size)

            while self.receiveSocketFD >= 0 {
                let bytesRead = withUnsafeMutablePointer(to: &senderAddr) {
                    $0.withMemoryRebound(to: sockaddr.self, capacity: 1) {
                        recvfrom(self.receiveSocketFD, &buffer, buffer.count, 0, $0, &addrLen)
                    }
                }

                if bytesRead > 0 {
                    let data = Data(bytes: buffer, count: bytesRead)
                    if let message = String(data: data, encoding: .utf8) {
                        self.log("受信メッセージ: \(message)")
                        self.bufferMessage(message)
                    }
                } else {
                    perror("受信失敗")
                }
            }
        }
        log("UDP受信開始（ポート: \(port)）")
    }

    @objc public func stopBroadcasting() {
        if socketFD >= 0 {
            close(socketFD)
            socketFD = -1
            log("送信用ソケット閉じた")
        }
        if receiveSocketFD >= 0 {
            close(receiveSocketFD)
            receiveSocketFD = -1
            log("受信用ソケット閉じた")
        }
    }

    // MARK: - ネットワークユーティリティ
    
    @objc public func getIPAddressForUnity() -> String {
        return getIPAddress() ?? ""
    }

    private func getIPAddress() -> String? {
        var ifaddrPtr: UnsafeMutablePointer<ifaddrs>?
        var candidates: [String] = []

        if getifaddrs(&ifaddrPtr) == 0 {
            var ptr = ifaddrPtr
            while ptr != nil {
                defer { ptr = ptr?.pointee.ifa_next }
                let interface = ptr!.pointee

                let addrFamily = interface.ifa_addr.pointee.sa_family
                guard addrFamily == UInt8(AF_INET) else { continue }

                let addr = interface.ifa_addr.withMemoryRebound(to: sockaddr_in.self, capacity: 1) { $0.pointee }
                let ip = inet_ntoa(addr.sin_addr)
                let ipStr = String(cString: ip!)

                if ipStr.hasPrefix("192.") {
                    let name = String(cString: interface.ifa_name)
                    if name.hasPrefix("en") {
                        freeifaddrs(ifaddrPtr)
                        return ipStr // 優先して返す
                    }
                    candidates.append(ipStr)
                }
            }
            freeifaddrs(ifaddrPtr)
        }

        return candidates.first // 何かしら192.x.x.xがあれば返す
    }

    private func calculateBroadcastAddress(ip: String, subnetMask: String) -> String {
        func ipToUInt32(_ ip: String) -> UInt32 {
            return ip.split(separator: ".").map { UInt32($0)! }.reduce(0) { ($0 << 8) + $1 }
        }
        let ipVal = ipToUInt32(ip)
        let maskVal = ipToUInt32(subnetMask)
        let broadcastVal = (ipVal & maskVal) | ~maskVal

        return [
            (broadcastVal >> 24) & 0xFF,
            (broadcastVal >> 16) & 0xFF,
            (broadcastVal >> 8) & 0xFF,
            broadcastVal & 0xFF,
        ].map { String($0) }.joined(separator: ".")
    }
}
