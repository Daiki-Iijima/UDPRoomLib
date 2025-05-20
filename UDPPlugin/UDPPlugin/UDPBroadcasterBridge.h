#ifndef UDPBroadcasterBridge_h
#define UDPBroadcasterBridge_h

#ifdef __cplusplus
extern "C" {
#endif

void StartUDPBroadcastWithPort(int port);
void StartUDPReceivingWithPort(int port);

void StartUDPBroadcast(void);
void StartUDPReceiving(void);

void StopUDPBroadcast(void);
void SendUDPMessage(const char *message);

const char *PollUDPMessage(void);

const char *GetLocalIPAddress(void);

#ifdef __cplusplus
}
#endif

#endif /* UDPBroadcasterBridge_h */
