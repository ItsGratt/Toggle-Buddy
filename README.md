# Toggle Goat

A tiny Windows desktop buddy that shows whether Num Lock and Caps Lock are on.

## Run

Extract the ZIP into a folder you can write to, such as Documents. Double-click **ToggleGoat.exe**. Keep **ToggleGoat.exe.config** beside it. No installer, account, network connection, or administrator access is required. Windows 10/11 with .NET Framework 4.8 is required.

The goat sits in the bottom-right corner of the rightmost screen, eight logical pixels above the taskbar. It starts at 144 logical pixels (approximately 1.5 inches); actual physical inches depend on your monitor and Windows scaling. The square is a maximum area: wide artwork keeps its original proportions instead of stretching.

| Num Lock | Caps Lock | Drawing |
|---|---|---|
| Off | Off | Sleeping / idle |
| On | Off | NUM |
| Off | On | CAP |
| On | On | NUM and CAP |

**Right-click the buddy → Settings** to change pictures, preview a state, adjust size, or select a screen. Double-clicking the buddy also opens Settings. **Save settings** keeps your changes; **Cancel** restores the previous settings. Preview temporarily overrides the buddy's drawing, not your keyboard. Use **Return buddy to live keys**, Save, or Cancel to end a preview.

Right-click the goat icon in the system tray for **Settings, Hide/Show, or Quit**. Windows may place that icon under the tray's overflow arrow. The buddy doesn't have a taskbar button and doesn't take keyboard focus when it updates. Opening the executable again brings up Settings instead of creating a second buddy.

## Your own pictures

Each state has its own **Choose**, **Preview**, and **Reset** controls. Transparent PNGs look best. JPG, BMP, and GIF are accepted; GIFs are displayed as still images. Custom opaque images keep their backgrounds. Images must be smaller than 32 MB.

The supplied drawings have been prepared with transparent surroundings and white interiors. The original uploads are preserved in **Originals**. Pictures are fitted without distortion, and transparent padding is ignored for display.

Chosen pictures are copied into the **Data** folder beside the executable when you save, so moving the original image doesn't break the app. Settings are stored in **Data/settings.xml**. Keep Data with the app when moving your personalized copy. A missing or unreadable picture falls back to the built-in drawing. Reset restores the bundled artwork. Old imported pictures are retained in Data rather than deleted automatically.

## Start with Windows (optional)

Create a shortcut to ToggleGoat.exe and place that shortcut in your Windows user Startup folder. This version does not add itself to startup automatically. Quit from the tray when you want it to stop running.

## Share and modify

Share **ToggleGoat.zip**, or share the extracted folder. The four default drawings are embedded in the executable, so the Artwork and Source folders are only needed to rebuild or edit the project. A freshly shared ZIP does not include your saved Data folder.

Source code is in **Source/ToggleGoat.cs**. The code is MIT licensed; see **LICENSE.txt**. The drawings are supplied by the project's artist; the code license does not grant rights to their artwork. Obtain the artist's permission for separate redistribution of the art.

To rebuild on Windows, run **Source/build.ps1** in PowerShell. The script uses the .NET Framework C# compiler included with Windows and needs no NuGet packages. Quit all running instances from this folder before rebuilding. The executable is unsigned.

## Implementation

- C# and WPF on .NET Framework 4.8; Windows Forms supplies the tray icon and monitor work areas.
- Transparent, borderless, topmost window with no activation on ordinary clicks or updates.
- Checks only the Num Lock and Caps Lock toggle flags every 125 ms. No typed text is captured or saved. No keyboard hook or network calls.
- Artwork is decoded once per load; the displayed image changes only when needed.
- Uses the selected display's working area to avoid its taskbar. Repositions after display/scaling changes and periodically checks placement.
- Settings changes are portable; imported picture paths are saved relative to Data.

## Development checks

Run the executable with `--self-test --data-dir <test-folder>` and wait for it to exit. Results are written to `self-test.txt` in that folder. Tests cover the four-state mapping, embedded alpha images, settings persistence, image import/reset, portable paths, and invalid-settings recovery. Use a scratch folder: this test intentionally overwrites its own settings fixture.

`--render-test --data-dir <test-folder>` renders settings and the four image states into PNG previews. `--diagnostics <file>` optionally writes only the lock-state name and buddy layout when they change. `--settings` opens Settings at launch. `--data-dir <folder>` selects a separate portable profile. Normal launch does not write diagnostics.

The overlay is intended for the normal Windows desktop. Secure screens and exclusive full-screen applications may cover it. Exact physical sizing and mixed-DPI behavior can vary by monitor setup.
