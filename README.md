# Screen Painter

**Screen Painter** is robust android only custom wallpaper rotation application, this application support both local and cloud storage for wallpaper rotation


## Feature
**Home Screen and Lock Screen Wallpaper Rotation**
 - Can set rotation on both Home Screen and Lock Screen separately
 - Can set rotation to either use timer or screen wake or both
 
 **Support both Local and Cloud Storage**
 
 - Support Local Phone Storage for Wallpaper Rotation
 - Support Cloud that use WebDav Format like pCloud
 - Support Cloud that use OAuthorization like Google Drive
 - No credential information upload , keep in the safe place inside your phone.
 - Every credential information is properly encrypt

**Frame Adjustment**

 - Each collection can be adjust frame ratio and offset
 - Each image can be adjust frame ratio and offset even those in the cloud

**Smart Auto-Framing (opt-in)**

 - A picture that is already your phone's own size, or already its shape, is applied exactly as
   it is. Nothing is analysed and nothing is moved — a wallpaper the right shape cannot be
   improved by cropping, only spoiled
 - Anything else is analysed on the device to find the face of the person or character in it,
   and the frame is **panned** to follow the subject. The crop is never made tighter than what
   covering your screen requires, so no part of the picture is ever thrown away to improve the
   framing
 - The frame moves sideways only when the subject would otherwise be cut by a screen edge, and
   only as far as clearing that edge needs. A subject already comfortably in frame is left
   exactly where the photographer put it
 - The frame moves up or down only when the head comes within the headroom of an edge
 - The crop is computed from the wallpaper surface of the actual phone, so a 1080x2400 phone
   and a 1440x3120 phone each get their own correct result
 - The subject is kept in frame — a held prop or gesture is not cropped away — and the
   picture always fills the screen, so no black bands can appear
 - A framing you set by hand for a specific image always takes priority over auto-framing
 - Works for local **and** cloud collections. Detection runs on the already-downloaded copy
   and needs no network, so it also works from the background rotation service

 **What it will not do**

 - It never zooms in. A distant or full-body subject is therefore left as the photographer
   framed it rather than being enlarged toward the centre of the screen: enlarging is a crop,
   and a crop discards picture
 - It never zooms out past filling the screen either, so no black band can ever be introduced
 - Because the crop is only ever panned, a picture gets exactly one axis of freedom — the axis
   its shape differs from your screen on. A photo wider than your screen can be moved sideways
   but not up and down, and a photo narrower than it the other way round

 **Turning it on**

 - Per collection: **Edit Collection → Smart Auto-Framing**
 - Default for new collections: **Settings → Smart Auto-Framing**
 - **Settings → Clear Analysis History** forgets everything the app has analysed so far

**How it works, and what it costs**

 - Face detection runs fully offline through MediaPipe Tasks with a bundled ~230 KB BlazeFace
   model. No image, and nothing derived from one, ever leaves the device
 - Each wallpaper is analysed **once**; the result is remembered on the device, so later
   rotations apply instantly without re-analysing
 - Because the model ships native code for each CPU type, the APK grows by roughly 10 MB
   (64-bit ARM) to 13 MB (x86_64) per included architecture. Only Android builds include it,
   and a release limited to `arm64-v8a` carries none of the other architectures
 - The minimum supported Android version is **7.0 (API 24)**, which is what the detection
   library requires