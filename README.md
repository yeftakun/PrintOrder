# PrintOrder Client

PrintOrder Client adalah aplikasi desktop Windows yang digunakan oleh mitra percetakan untuk menerima dan memproses tugas cetak dari sistem PrintOrder.

Aplikasi ini terhubung dengan server PrintOrder, menerima tugas cetak dari pelanggan, lalu membantu pihak percetakan mengelola antrean dan mencetak dokumen melalui printer lokal yang tersedia pada komputer pengguna.

## Tentang PrintOrder

PrintOrder adalah platform layanan cetak dokumen yang membantu pelanggan mengirim dokumen ke percetakan secara lebih terstruktur. Pelanggan dapat membuka halaman toko, mengunggah dokumen, mengatur spesifikasi cetak, dan mengirim tugas cetak tanpa perlu mengirim file melalui WhatsApp, flashdisk, atau media lainnya.

PrintOrder Client merupakan bagian dari sistem tersebut yang berjalan di sisi percetakan. Aplikasi ini digunakan untuk menerima, memantau, dan memproses tugas cetak yang masuk dari pelanggan.

## Peran Aplikasi

PrintOrder Client berfungsi sebagai penghubung antara server PrintOrder dan printer lokal di tempat percetakan.

Secara umum, aplikasi ini digunakan untuk:

* Menghubungkan komputer percetakan dengan akun mitra PrintOrder.
* Mendaftarkan perangkat client ke server.
* Menampilkan printer yang tersedia pada komputer.
* Menerima tugas cetak dari pelanggan.
* Menampilkan daftar tugas cetak yang masuk.
* Memproses, menolak, atau memperbarui status tugas cetak.
* Menjaga status client agar server mengetahui apakah percetakan sedang siap menerima tugas cetak.
* Menampilkan notifikasi ketika ada tugas cetak baru.
* Tetap berjalan di latar belakang melalui system tray.

## Fitur Utama

### Pairing Akun Mitra

Aplikasi dapat dihubungkan dengan akun mitra PrintOrder. Setelah pairing berhasil, client desktop akan dikenali sebagai perangkat milik toko percetakan terkait.

### Daftar Tugas Cetak

Aplikasi menampilkan daftar tugas cetak yang dikirim pelanggan. Pihak percetakan dapat melihat informasi dokumen dan spesifikasi cetak sebelum memprosesnya.

### Pemrosesan Cetak

Tugas cetak yang masuk dapat diproses melalui printer lokal yang tersedia pada komputer. Status tugas cetak akan diperbarui agar pelanggan dan sistem mengetahui perkembangan proses cetak.

### Notifikasi

Aplikasi mendukung notifikasi ketika ada tugas cetak baru, sehingga pihak percetakan dapat segera mengetahui adanya pekerjaan yang masuk.

### Pengaturan Client

Aplikasi menyediakan pengaturan dasar seperti alamat server, informasi client, opsi notifikasi, auto-start Windows, dan status pendukung cetak.

### System Tray

Aplikasi tetap dapat berjalan di latar belakang ketika jendela utama ditutup, sehingga client tetap siap menerima tugas cetak selama aplikasi masih aktif.

## Kebutuhan Penggunaan

Untuk menggunakan PrintOrder Client, dibutuhkan:

* Perangkat dengan sistem operasi Windows.
* Printer lokal yang sudah terpasang dan dapat digunakan.
* Koneksi ke server PrintOrder.
* Akun mitra PrintOrder.
* Aplikasi PrintOrder Client yang sudah terpasang.

## Alur Penggunaan Umum

1. Mitra menginstal dan membuka aplikasi PrintOrder Client.
2. Mitra memastikan alamat server sudah sesuai.
3. Mitra menghubungkan aplikasi dengan akun PrintOrder.
4. Mitra memilih atau memastikan printer lokal yang digunakan.
5. Aplikasi mulai terhubung ke server.
6. Ketika pelanggan mengirim tugas cetak, tugas tersebut muncul pada daftar tugas.
7. Pihak percetakan memeriksa tugas cetak.
8. Pihak percetakan memproses atau menolak tugas cetak sesuai kondisi.
9. Status tugas cetak diperbarui ke server.
10. Aplikasi tetap berjalan di latar belakang untuk menerima tugas berikutnya.

## Hubungan dengan Komponen Lain

PrintOrder Client merupakan salah satu bagian dari ekosistem PrintOrder.

| Komponen          | Keterangan                                                                           |
| ----------------- | ------------------------------------------------------------------------------------ |
| PrintOrder Server | Mengelola data akun, toko, sesi cetak, tugas cetak, billing, dan komunikasi realtime |
| Portal Mitra      | Digunakan mitra untuk mengelola toko, layanan, billing, dan akun                     |
| Halaman Pelanggan | Digunakan pelanggan untuk mengunggah dokumen dan membuat tugas cetak                 |
| PrintOrder Client | Digunakan percetakan untuk menerima dan memproses tugas cetak dari pelanggan         |

## Status Proyek

Aplikasi ini dikembangkan sebagai bagian dari proyek tugas akhir/skripsi dengan fokus pada perancangan dan pembangunan platform cetak dokumen pada layanan percetakan.

## Dokumentasi Internal

Dokumentasi teknis tambahan dapat disimpan pada direktori `docs/`.

Dokumentasi teknis tidak dimaksudkan sebagai dokumentasi publik, melainkan sebagai catatan pengembangan dan pemeliharaan sistem.

## Catatan Hak Akses

[LICENCE](LICENCE)

## Ringkasan

PrintOrder Client membantu mitra percetakan menerima dan memproses tugas cetak dari pelanggan secara lebih terstruktur. Aplikasi ini menjadi penghubung antara server PrintOrder dan printer lokal, sehingga proses cetak dapat dikelola melalui sistem yang lebih rapi dan terintegrasi.