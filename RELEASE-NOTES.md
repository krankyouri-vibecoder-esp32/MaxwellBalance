# MaxwellBalance 0.2b

Wireless compatibility update for Windows x64.

- Wireless operation confirmed on Maxwell 2 Xbox dongle (3329:4B28), including user-confirmed wheel response.
- Headset and dongle firmware **v1.0.5.9.1** read directly from both devices on 18 November 2026; this was the latest version listed by Audeze at the time.
- Handles fragmented and combined RACE replies, with a 1.5-second response timeout and 50 ms read intervals.
- Adds regression checks for fragmented and combined replies.
- Documents recovery when audio still works but the control channel stops answering: unplug the dongle for five seconds and reconnect with the headset on. Cause remains undetermined; no automatic device reset.

The quiet startup, bottom-center overlay, tray menu and focus-preserving topmost behavior remain unchanged. Exclusive fullscreen visibility is not guaranteed. Dutch interface; unsigned, unofficial beta.

## 0.1b

First beta release for Windows x64.

- Displays the Maxwell 2 game/chat balance when the wheel changes.
- Bottom-center indicator, hidden after four seconds.
- Tray controls to show, keep visible, and exit.
- Quiet startup and reconnection; confirms changes across two readings to suppress transient popups.
- Reasserts topmost position while visible without taking game focus.
- Read-only headset communication; no audio routing changes or additional drivers.

Validated: direct USB hardware read, user-confirmed wheel response, protocol parsing and change-detection checks.

Known limitations: wireless-only operation remains unverified; exclusive fullscreen visibility is not guaranteed. Dutch interface. Unsigned, unofficial beta.
