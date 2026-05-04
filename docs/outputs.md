# Output targets

Voxto can write each transcription to one or more enabled output targets at the same time.

## Markdown files

- Creates one `.md` file per recording in the configured output folder.
- Best when you want an archive of every transcription.

Example format:

```markdown
# Transcription

**Date:** Friday, April 24, 2026
**Time:** 14:32:10

---

`00:00:01` → `00:00:04`
Hello, this is the first segment.

`00:00:04` → `00:00:09`
And this is the second segment.
```

## Todo list

- Appends each transcription as a single unchecked task to one shared Markdown file.
- Best when you want spoken notes to land in an existing todo document.

## Cursor location

- Inserts the transcription into the currently focused application at the active cursor location.
- Uses the transcription's single-line full text, so empty segments are ignored and segment text is joined with spaces.
- Optional setting: press Enter immediately after inserting the text.

This output is useful for dictating directly into editors, chat inputs, AI agents, or other text fields without switching windows.
