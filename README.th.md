# Sound2Text

[English](README.md) | ไทย

ถอดเสียงไทย + อังกฤษ (ในไฟล์เดียวกันได้) แบบ local ด้วย Whisper ผ่าน Whisper.net บน .NET 10 รันเป็น container ได้ทั้งแบบ GPU และ CPU (NAS)

## ความสามารถ
- อัปโหลดไฟล์เสียง/วิดีโอ (mp3, m4a, wav, mp4 ฯลฯ ผ่าน ffmpeg) ยาว 15–30 นาทีขึ้นไป
- เลือกภาษาได้ต่อไฟล์: `th` (ค่าเริ่มต้น, มีอังกฤษปนได้), `en`, `auto` (ตรวจทีละท่อน)
- ช่อง hint ใส่ศัพท์อังกฤษ/ชื่อที่จะพูดถึง เพื่อช่วยให้ถอดแม่นขึ้น
- คิวงานทีละงาน แสดง progress ยกเลิก/ลบรายงาน และล้างคิวได้
- Export TXT / SRT / VTT
- ยังไม่รองรับการแยกผู้พูด (speaker diarization)

## Models
โหลดด้วย `.\scripts\get-model.ps1 -Name <ไฟล์>` (เก็บใน `models/`, ไม่ commit)

| ไฟล์ | ใช้เมื่อ |
|---|---|
| `ggml-large-v3-q5_0.bin` | แม่นที่สุด (ค่าเริ่มต้นของ GPU) ~1 GB |
| `ggml-medium-q5_0.bin` | ค่าเริ่มต้นของ NAS/CPU เร็วประมาณ 2 เท่าของ large-v3 ~0.5 GB |
| `ggml-small-q5_1.bin` | CPU ช้ามาก ความแม่นยำภาษาไทยลดลง |

## รันด้วย GPU (NVIDIA)
ต้องมี NVIDIA driver ที่รองรับ **CUDA 13** (Windows: Docker Desktop + WSL2, ไม่ต้องลง CUDA เอง)

```
.\scripts\get-model.ps1
docker compose up -d --build
```
เปิด `http://localhost:8080`

ตรวจว่า container เห็น GPU: `docker run --rm --gpus all nvidia/cuda:13.0.2-base-ubuntu24.04 nvidia-smi`
ถ้า CUDA ใช้ไม่ได้ Whisper.net จะตกไปรันบน CPU เงียบๆ (ช้ากว่ามาก) ให้ดูว่า VRAM ขึ้นตอนมีงานรัน

## รันบน NAS / CPU เท่านั้น (Intel x86)
```
.\scripts\get-model.ps1 -Name ggml-medium-q5_0.bin
docker compose -f docker-compose.nas.yml up -d --build
```
- image ~660 MB (ไม่มี CUDA)
- แก้ volume ใน `docker-compose.nas.yml` ให้ชี้โฟลเดอร์ของ NAS (เช่น `/volume1/docker/sound2text/models` และ `.../data`) และตั้ง `Whisper__Threads` เท่าจำนวน core
- ขึ้น NAS ได้โดย `docker save sound2text:cpu -o s2t.tar` แล้วโหลดใน Container Manager / Container Station หรือคัดลอกโปรเจกต์ไป build บน NAS
- ถ้าอยู่หลัง reverse proxy ให้เพิ่ม upload size และ timeout (ไฟล์ได้ถึง 500 MB)

ความเร็วที่วัดได้ (พีซี 16 core จำกัด 4 CPU / 8 GB, คลิปเสียงอังกฤษสังเคราะห์ 79 วินาที): medium q5 72 วินาที, large-v3 q5 146 วินาที RAM ~1–1.2 GB
NAS ช้ากว่านี้ ควรวัดบนเครื่องจริง ไฟล์ 30 นาทีอาจใช้หลายชั่วโมงแนะนำรันข้ามคืน

## Config (environment)
| ตัวแปร | ค่าเริ่มต้น | ความหมาย |
|---|---|---|
| `Whisper__ModelPath` | `/models/ggml-large-v3-q5_0.bin` (GPU), `/models/ggml-medium-q5_0.bin` (CPU) | ไฟล์โมเดล |
| `Whisper__UseGpu` | `true` / `false` (CPU image) | ใช้ GPU หรือไม่ |
| `Whisper__Threads` | `4` | จำนวน thread บน CPU |
| `Whisper__DataDir` | `/data` | ที่เก็บไฟล์อัปโหลดและฐานข้อมูล SQLite |
| `Whisper__MaxUploadMb` | `500` | ขนาดไฟล์สูงสุด |

Volumes: `./models` (อ่านอย่างเดียว) และ `./data` (uploads + `jobs.db`)

## API
| Method | Path | |
|---|---|---|
| POST | `/api/jobs` | multipart: `file`, `language` (`th`/`en`/`auto`), `hint` → `{id}` |
| GET | `/api/jobs` | รายการงานล่าสุด 100 งาน |
| GET | `/api/jobs/{id}` | สถานะ, progress, segments (start/end/text) |
| GET | `/api/jobs/{id}/export?fmt=txt\|srt\|vtt` | ดาวน์โหลดผลลัพธ์ |
| POST | `/api/jobs/{id}/cancel` | ยกเลิกงานที่รออยู่หรือกำลังรัน |
| DELETE | `/api/jobs/{id}` | ลบงานและไฟล์ที่อัปโหลด |
| POST | `/api/jobs/clear-queue` | ยกเลิกงานที่รอและงานที่กำลังรันทั้งหมด |
| GET | `/healthz` | สถานะ, `gpu` (ตั้งค่าไว้หรือไม่), `model` (พบไฟล์โมเดลหรือไม่) |

สถานะงาน: `Queued`, `Running`, `Done`, `Failed`, `Cancelled`
ถ้า container ถูก restart ระหว่างงานกำลังรัน งานจะถูกคิวใหม่และรันต่อเอง ถ้าถูกยกเลิกจะเป็น `Cancelled` ไฟล์ที่ `Failed` ต้องอัปโหลดใหม่

## โครงสร้าง
```
src/Sound2Text.Api/   ASP.NET Core minimal API + wwwroot (หน้าเว็บ)
  Services/           Transcriber (Whisper.net), Chunker, AudioService (ffmpeg), TranscriptionWorker, Exporter
  Data/               Job, AppDb (SQLite)
docker/Dockerfile     GPU (CUDA 13 runtime)
docker/Dockerfile.cpu CPU / NAS
scripts/get-model.ps1
```

## หมายเหตุ
- ถอดทีละท่อนไม่เกิน 30 วินาที ตัดที่ช่วงเงียบ ท่อนที่เงียบทั้งท่อนจะข้าม และตัดประโยคซ้ำติดกันออก
- ทดสอบความถูกต้องของไทย/ไทยปนอังกฤษกับไฟล์จริงเพิ่มเติมได้ ถ้า large-v3 ไม่แม่นพอ พิจารณาโมเดลไทย fine-tune ผ่าน `ITranscriber`
