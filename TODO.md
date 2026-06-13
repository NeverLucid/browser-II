# TODO - Hold-to-Back/Forward History Preview

- [x] Implement hold-to-preview dropdown for Back and Forward buttons in `MyBrowserShell/Form1.cs`.
- [x] Use WebView2 navigation history (via CoreWebView2.GetNavigationHistory when available) to populate menu.
- [x] Support safe fallback when jump methods are not available (use sequential GoBack/GoForward).
- [x] Ensure regular click behavior remains unchanged when hold duration is not reached.
- [ ] Build/run and do a quick manual verification: hold back shows entries; click item navigates; same for forward.
