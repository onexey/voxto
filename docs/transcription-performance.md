# Transcription performance

Voxto now keeps the currently selected Whisper model loaded for the lifetime of the app. The first transcription after launch or after changing models still pays the model-initialization cost, but later transcriptions reuse the in-memory model instead of rebuilding it every time.

Windows builds now also ship additional Whisper runtimes so inference can accelerate automatically when the machine supports them:

- **CUDA** for supported NVIDIA GPUs
- **Vulkan** for supported Windows x64 GPUs
- **OpenVINO** for supported Intel hardware

Voxto does not call these runtime packages directly in C#. They add native runtime assets that Whisper.net probes automatically when `WhisperFactory` is created.

Whisper.net already uses the machine's available hardware threads by default, so no extra multi-core setting is required in Voxto.

If the required GPU/runtime dependencies are not available on the machine, Whisper falls back to the CPU runtime automatically.
