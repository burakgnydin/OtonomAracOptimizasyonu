# Otonom Araç Trafik Optimizasyonu ve Simülasyonu

Bu proje, tek şeritli bir yolda hareket eden otonom araçların hareketlerini optimize etmek için tasarlanmış bir ayrık olay simülasyon sistemidir. [cite_start]Proje, matematiksel modelleme ve öncelik tabanlı sezgisel algoritmalar kullanarak **çarpışma önleme** ve **kilitlenme (deadlock) çözümü** gibi gerçek dünya lojistik sorunlarına odaklanmaktadır[cite: 4, 5].

## 🚀 Öne Çıkan Özellikler

- [cite_start]**Dinamik Senaryo Yönetimi:** Yol yapılandırmalarını ve araç görevlerini JSON dosyaları üzerinden yükleme imkanı[cite: 44, 63].
- [cite_start]**Gelişmiş Trafik Kontrolörü:** Güvenlik kısıtlarını sağlamak için sensör verilerinin gerçek zamanlı izlenmesi[cite: 12, 17].
- [cite_start]**Akıllı Manevra Modu:** Kilitlenmeleri çözmek için düşük öncelikli araçların en yakın "Güvenli Alan"a (Cep veya Depo) otonom olarak çekilmesi[cite: 22, 33, 36].
- **Ölçeklenebilir Mimari:** Farklı optimizasyon algoritmalarına izin veren SOLID prensipleri ve Strateji Tasarım Deseni (Strategy Pattern) ile inşa edilmiş yapı.

## 🛠 Teknik Özellikler

- **Dil:** C# / .NET
- [cite_start]**Yol Geometrisi:** 40 metrede bir sensör bulunan 240 metrelik parkur[cite: 15, 16, 18].
- [cite_start]**Güvenlik Mesafesi ($d_{min}$):** Tüm araçlar arasında minimum 20 metre güvenlik mesafesi[cite: 31].
- **Kaynak Yönetimi:**
  - [cite_start]**Cepler (Pockets):** 1 araç kapasiteli bekleme alanları[cite: 20, 37].
  - [cite_start]**Depolar (Depots):** 3 araç kapasiteli, aynı zamanda güvenli liman (cep) olarak kullanılabilen alanlar[cite: 21, 22, 37].

## 📊 Çalışma Mantığı

Simülasyon "Tick" (zaman adımı) bazlı çalışır. Her döngüde `TrafficController` şunları gerçekleştirir:
1. [cite_start]Araçlar arasındaki mesafeyi değerlendirir[cite: 31, 32].
2. [cite_start]Karşı karşıya gelme veya çarpışma risklerini kontrol eder[cite: 11, 17].
3. [cite_start]Bir kilitlenme (deadlock) algılandığında, düşük öncelikli araca bir **Manevra Görevi** atayarak onu en yakın boş cebe veya depoya gitmesi için yönlendirir[cite: 33, 36].

## 👥 Katkıda Bulunanlar

[cite_start]Bu proje, **Karadeniz Teknik Üniversitesi, Bilgisayar Mühendisliği Bölümü** Optimizasyon dersi kapsamında geliştirilmiştir[cite: 1, 2]:

## 📄 Lisans

[cite_start]Bu proje, KTÜ 2025/26 akademik yılı kapsamında eğitim amaçlı üretilmiştir ve izinsiz kullanılmamalıdır[cite: 2].
