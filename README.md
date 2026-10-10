# Sound2Text

English | [ไทย](README.th.md)

Local Thai + English speech-to-text (a single file can mix both languages) using Whisper via Whisper.net on .NET 10. Runs as a container on an NVIDIA GPU or on CPU only (e.g. a NAS).

## Features
- Upload audio/video (mp3, m4a, wav, mp4, ... decoded by ffmpeg); built for 15–30 minute recordings
- Per-file language: `th` (default, English words allowed), `en`, or `auto` (detected per chunk)
- Optional hint field for English terms or names that will appear in the audio
- One-at-a-time job queue with progress, cancel, delete and clear-queue
- Export as TXT / SRT / VTT
- No speaker diarization yet

## Models
Download with `.\scripts\get-model.ps1 -Name <file>` (saved to `models/`, not committed).

| File | Use |
|---|---|
| `ggml-large-v3-q5_0.bin` | Best accuracy (GPU default), ~1 GB |
| `ggml-medium-q5_0.bin` | NAS / CPU default, about twice as fast as large-v3, ~0.5 GB |
| `ggml-small-q5_1.bin` | Very slow CPUs only; noticeably worse Thai |

## Run with an NVIDIA GPU
Requires an NVIDIA driver that supports **CUDA 13** (on Windows: Docker Desktop with the WSL2 backend; no separate CUDA install).

```
.\scripts\get-model.ps1
docker compose up -d --build
```
Open `http://localhost:8080`.

Check that containers can see the GPU:
`docker run --rm --gpus all nvidia/cuda:13.0.2-base-ubuntu24.04 nvidia-smi`

If CUDA cannot be loaded, Whisper.net silently falls back to the CPU (much slower). Watch VRAM in `nvidia-smi` while a job runs to confirm the GPU is used.

## Run on a NAS / CPU only (Intel x86)
```
.\scripts\get-model.ps1 -Name ggml-medium-q5_0.bin
docker compose -f docker-compose.nas.yml up -d --build
```
- The CPU image is ~660 MB (no CUDA).
- Edit the volumes in `docker-compose.nas.yml` to point at NAS shared folders (e.g. `/volume1/docker/sound2text/models` and `.../data`) and set `Whisper__Threads` to the core count.
- To move the image: `docker save sound2text:cpu -o s2t.tar`, then load it in Container Manager / Container Station; or copy the project to the NAS and build there.
- Behind a reverse proxy, raise the upload size limit and timeouts (files up to 500 MB).

Measured on a 16-core PC limited to 4 CPUs / 8 GB with a 79 s synthetic English clip: medium q5 72 s, large-v3 q5 146 s, RAM ~1–1.2 GB. NAS CPUs are slower, so benchmark on the device. A 30-minute file may take hours; run it overnight.

## Configuration (environment variables)
| Variable | Default | Meaning |
|---|---|---|
| `Whisper__ModelPath` | `/models/ggml-large-v3-q5_0.bin` (GPU), `/models/ggml-medium-q5_0.bin` (CPU) | Model file |
| `Whisper__UseGpu` | `true` / `false` (CPU image) | Use the GPU |
| `Whisper__Threads` | `4` | CPU threads |
| `Whisper__DataDir` | `/data` | Uploads and SQLite database |
| `Whisper__MaxUploadMb` | `500` | Max upload size |

Volumes: `./models` (read-only) and `./data` (uploads + `jobs.db`).

## API
| Method | Path | Description |
|---|---|---|
| POST | `/api/jobs` | multipart: `file`, `language` (`th`/`en`/`auto`), `hint` → `{id}` |
| GET | `/api/jobs` | Latest 100 jobs |
| GET | `/api/jobs/{id}` | Status, progress, segments (start/end/text) |
| GET | `/api/jobs/{id}/export?fmt=txt\|srt\|vtt` | Download the transcript |
| POST | `/api/jobs/{id}/cancel` | Cancel a queued or running job |
| DELETE | `/api/jobs/{id}` | Delete the job and its uploaded file |
| POST | `/api/jobs/clear-queue` | Cancel all queued jobs and the running one |
| GET | `/healthz` | `gpu` (configured), `model` (model file found) |

Job states: `Queued`, `Running`, `Done`, `Failed`, `Cancelled`.
If the container restarts while a job is running, the job is requeued and resumes by itself. Cancelled jobs stay `Cancelled`. Jobs that already `Failed` must be uploaded again.

## Layout
```
src/Sound2Text.Api/    ASP.NET Core minimal API + wwwroot (web UI)
  Services/            Transcriber (Whisper.net), Chunker, AudioService (ffmpeg), TranscriptionWorker, Exporter
  Data/                Job, AppDb (SQLite)
docker/Dockerfile      GPU image (CUDA 13 runtime)
docker/Dockerfile.cpu  CPU / NAS image
scripts/get-model.ps1
```

## Notes
- Audio is split into chunks of at most 30 s at the quietest point; silent chunks are skipped and consecutive duplicate segments are dropped.
- Thai and mixed Thai/English accuracy has not been benchmarked on real recordings yet. If large-v3 is not accurate enough, consider a Thai fine-tuned model behind the `ITranscriber` interface.
