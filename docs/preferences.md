# Preferences

Voxto groups settings into dedicated tabs:

- **General** — model selection, hotkey behavior, startup, and update preferences.
- **Markdown** — enable the output and choose the folder for per-recording `.md` files.
- **Todo** — enable the output and choose the shared Markdown todo file.
- **Cursor** — enable direct insertion and optionally press Enter after the text is sent.
- **About** — version and repository links.

Each output owns its own settings page, metadata, and typed settings object. Preferences discovers those tabs directly from the registered outputs, so new add-ons can bring their own configuration without editing the main preferences flow.
