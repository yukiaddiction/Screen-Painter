# Screen Painter v1.3.1 — Bug Fix Release

**App version:** 1.3.1 · **Build:** 13

## 🐛 Bug Fixes

- **Wallpaper overflow after landscape apps:** Fixed wallpaper being applied with the device's *current* orientation instead of the natural portrait orientation. After using a landscape-locked app (e.g. video games), returning to the home screen or lock screen no longer shows an overflowing/zoomed-cropped wallpaper. The wallpaper is now always rendered at the portrait wallpaper-surface dimensions, regardless of the foreground app's orientation.

## 🔒 Security & Stability

- **Real credential encryption:** Cloud account credentials (WebDAV/username/password and OAuth tokens) are now genuinely encrypted (AES-256-CBC + HMAC-SHA256) with the ciphertext stored in the account file and only the master key kept in the platform keystore. Existing accounts remain readable via automatic migration on first decrypt; a failed decrypt now returns null and surfaces a re-auth prompt instead of silently losing credentials.
- **Fixed silent credential-loss path** where a SecureStorage write failure returned an unusable ciphertext that could never be decrypted again.
- **HTTPS enforced for cloud endpoints:** Plain-HTTP WebDAV URLs are rejected (or upgraded to HTTPS, except localhost) so credentials are never sent in cleartext.
- **Credentials no longer forwarded on cross-origin redirects** during WebDAV requests.
- Removed `usesCleartextTraffic` from the Android manifest.

## ⚙️ Reliability

- **Fixed crash-prone async commands:** All ViewModel commands now observe their async tasks, so an exception in a command handler shows an alert instead of crashing the app.
- **Boot stability:** Wallpaper rotation now survives reboots that happen before the user unlocks the device (watchdog alarm is armed even when storage is not yet accessible; `LOCKED_BOOT_COMPLETED` handled).
- **No more double polling loops:** `OnStartCommand` reinitialization is serialized so the background service can no longer start two timers.
- **Battery efficiency:** Adaptive polling (5s→15s/30s when idle), the service now stops itself when no collection is enabled, wake locks are only acquired when there is actual rotation work, and Usage Stats is queried only when OnVisible rotation is configured.
- **Data integrity:** Collection/account JSON corruption is quarantined (`.corrupt`) instead of silently failing every future write; gallery manifests are written atomically.
- **Connection-pool hygiene:** Disposed leaked `HttpResponseMessage` instances in WebDAV/OAuth providers to prevent socket exhaustion.
- **Hash-code contract fix:** `CloudAccount`/`FolderSource` now hash case-insensitively, matching their equality — fixes duplicate entries in hash-based collections.

## 🧹 Cleanup

- Removed dead `WallpaperRotationHelper` code.
- Removed Play-policy-risk `USE_EXACT_ALARM` permission (graceful inexact-alarm fallback kept).
- Reformatted the bloated `Layouts.xaml` (3,199 → 709 lines).
- Added CI pipeline (GitHub Actions) and `global.json` SDK pin.
- 7 new unit tests covering the hash-code fix and rotation-gate pruning (81 tests total).

## ✅ Verified

- `net9.0-android` and `net9.0-windows` build with 0 errors.
- All 81 unit tests pass.

---

**Install / update:** Download the new APK and install it over the previous version. Your collections and cloud accounts are preserved.
