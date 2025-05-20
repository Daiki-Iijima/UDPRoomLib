#import "UDPBroadcasterBridge.h"
#import "UDPPlugin-Swift.h"

void StartUDPBroadcast(void) {
    [[UDPBroadcaster shared] startBroadcastingWithPort:5000];
}

void StartUDPReceiving(void) {
    [[UDPBroadcaster shared] startReceivingWithPort:5000];
}

void StartUDPBroadcastWithPort(int port) {
    [[UDPBroadcaster shared] startBroadcastingWithPort:(uint16_t)port];
}

void StartUDPReceivingWithPort(int port) {
    [[UDPBroadcaster shared] startReceivingWithPort:(uint16_t)port];
}

void StopUDPBroadcast(void) {
    [[UDPBroadcaster shared] stopBroadcasting];
}

void SendUDPMessage(const char *message) {
    NSString *msg = [NSString stringWithUTF8String:message];
    [[UDPBroadcaster shared] sendWithMessage:msg];
}

const char *PollUDPMessage(void) {
    NSString *msg = [[UDPBroadcaster shared] getNextBufferedMessage];
    if (msg) {
        return strdup([msg UTF8String]); // 呼び出し側で free() 必須
    }
    return NULL;
}

const char *GetLocalIPAddress(void) {
    NSString *ip = [[UDPBroadcaster shared] getIPAddressForUnity];
    return strdup([ip UTF8String]); // Unity 側で Marshal.PtrToString して使う
}
