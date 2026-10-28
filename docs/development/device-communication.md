# Device communication

Paths in backticks are relative to the repository root unless stated otherwise.

## Discovery and compatibility

- Device discovery uses IPv4/IPv6 UDP multicast on port `49526`. Each pass sends a signed `0.2` announcement. The desktop's `KitopiaConfig.enableLegacyDeviceCompatibility` switch (Device sharing settings, off by default) additionally sends an unsigned `0.1` compatibility announcement on the next broadcast pass; legacy peers authenticate through their nonce challenge and a `0.1` signed response. Receiving legacy announcements remains supported. Both versions must discover each other's public keys before TCP/TLS identity validation can succeed. Chat send notifications localize connection, timeout and identity failures; raw exceptions remain in diagnostics. Data transport currently supports TCP with TLS and device public-key certificate validation; the current transport enum does not expose QUIC.
- Device communication is independent of account login. Clipboard sending is handled by the chat UI and platform contracts; do not assume the removed clipboard message API still exists.

## Shared contracts and UI

- Shared communication contracts: `src/Kitopia.Feature/Application/`, `Discovery/`, `Transport/`, `Identity/`, `Security/`, `Sessions/`, and `DeviceCommunicationRuntime.cs`.
- File transfer UI uses `FileTransferStatus`; local accept/reject/cancel actions publish the same events as remote updates. `FileTransferUpdatedEvent.LocalFilePath` carries an optional local save path for completed-file actions.
- Chat platform integration: `IChatPlatformService`, `IChatClipboardService`, `IChatAttachmentStore`, and `IChatNotificationSink` live in `src/Kitopia.Feature/Application/`. Device rows flag an authenticated discovery protocol older than `DeviceDiscoveryService.CurrentProtocolVersion`; their hover card enables and persists legacy compatibility through `IDeviceCommunicationSettings`, with host capability checks. Keep the highest authenticated protocol version for an unchanged TCP listener so dual announcements do not mislabel current peers; unknown versions have no warning. Mobile discovery mirrors must copy this version along with other device state.
