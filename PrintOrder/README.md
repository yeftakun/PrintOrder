# PrintOrder Client (.NET)

WinForms client for receiving and printing jobs from the PrintOrder server.

## Requirements

- Windows 10/11
- .NET 8 SDK
- A local printer installed

Optional (for better PDF printing):

- SumatraPDF (recommended). If not installed, the client falls back to Microsoft Edge.
- SumatraPDF is required for PDF page-range printing such as `1, 3-5`.

## Run

```bash
dotnet run --project .\PrintOrder\PrintOrder.csproj
```

Saat startup, jika `printorder.ini` atau `printorder.client-id` belum ada, aplikasi akan menampilkan konfirmasi pembuatan file.
Jika disetujui, aplikasi akan meminta izin `Run as administrator` agar file dapat dibuat (terutama bila aplikasi terpasang di folder yang butuh hak admin), lalu menampilkan notifikasi file apa saja yang berhasil dibuat.

Konfigurasi dan state auth disimpan di `%LocalAppData%\PrintOrder`, sedangkan ID client disimpan di `%ProgramData%\PrintOrder` agar stabil antar user Windows.

Untuk mengubah `base_url`, gunakan tombol `Pengaturan` di aplikasi. Saat menekan `Simpan`, aplikasi akan meminta izin administrator (UAC) untuk menyimpan `printorder.ini`.

Set server URL in this file:

```ini
[server]
base_url=https://printorder.web.id

[timeouts]
api_timeout_seconds=15
download_timeout_seconds=300
sumatra_print_timeout_seconds=300
edge_headless_print_timeout_seconds=120
edge_window_print_timeout_seconds=60
```

`download_timeout_seconds` controls job file download time and is intentionally longer than normal API calls. Increase `sumatra_print_timeout_seconds` or Edge print timeouts if very large PDFs need more time to finish the print process.

## What it does

- Registers to the server and sends heartbeat updates.
- Polls ping messages from the server.
- Connects to server realtime WebSocket channel (`ws://.../ws`) and subscribes to `jobs`, `clients`, `sessions` events.
- On websocket connect, client immediately sends presence identity (`action=identify`, `clientId`, `role=client`) so server can mark online/offline faster.
- Realtime job events trigger immediate Job List refresh; periodic polling remains as fallback.
- Uses a persistent client GUID stored in `printorder.client-id` next to the executable.
- Menyediakan window `Pengaturan` untuk edit nilai `[server] -> base_url` pada `printorder.ini`.
- Shows a Job List window:
  - `Print` for jobs with status `ready`
  - `Retry` for jobs with status `pending`
  - `Reject` for jobs with status `ready`
- Re-checks job status from the server before Print/Reject to avoid stale actions.

## Print behavior

- Jobs are downloaded from the server to a temp file, printed locally, then the temp file is overwritten and deleted on a best-effort basis.
- Images (JPG/PNG/BMP) are printed via `PrintDocument`.
- PDF and other file types are printed via SumatraPDF (preferred) or Edge.
- PDF page ranges such as `1, 3-5` are sent through SumatraPDF; Edge fallback is used only for full-page PDF printing.
- If the selected printer is offline, the job is set to `pending` (not sent to the spooler).
- Secure temp cleanup reduces recovery risk for PrintOrder temp files, especially on HDD, but does not guarantee unrecoverability on SSD/wear-leveling storage, Windows print spooler, Edge/Sumatra/cache files, backups, shadow copies, or files deleted before this feature existed.

## Job status notes

- `ready`: waiting to be printed.
- `printing`: client started processing.
- `done`: sent to the printer spooler (UI label shows "sent").
- `pending`: printer offline at the moment of print.
- `failed`: download/print failure.
- `rejected`: rejected by the client.
- `canceled`: canceled by the web UI.

## Client identity notes

- The client reuses the same GUID on every restart (`printorder.client-id`).
- The app does not call unregister on close; server presence should be derived from heartbeat timeout (`last_seen_at` + TTL).

## Troubleshooting

- If PDF printing fails, install SumatraPDF and try again.
- If large PDFs fail during download or printing, increase the related `[timeouts]` values in `%LocalAppData%\PrintOrder\printorder.ini`.
- If the client does not appear online, make sure the server is running and `base_url` in `printorder.ini` is correct.
