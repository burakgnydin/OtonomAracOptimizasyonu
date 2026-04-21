# Otonom Araç Trafik Optimizasyonu ve Simülasyonu

Bu proje, tek şeritli bir yolda hareket eden otonom araçların hareketlerini optimize etmek için tasarlanmış bir ayrık olay simülasyon sistemidir.Proje, matematiksel modelleme ve öncelik tabanlı sezgisel algoritmalar kullanarak **çarpışma önleme** ve **kilitlenme (deadlock) çözümü** gibi gerçek dünya lojistik sorunlarına odaklanmaktadır.

## 🚀 Öne Çıkan Özellikler

- **Dinamik Senaryo Yönetimi:** Yol yapılandırmalarını ve araç görevlerini JSON dosyaları üzerinden yükleme imkanı.
- **Gelişmiş Trafik Kontrolörü:** Güvenlik kısıtlarını sağlamak için sensör verilerinin gerçek zamanlı izlenmesi.
- **Akıllı Manevra Modu:** Kilitlenmeleri çözmek için düşük öncelikli araçların en yakın "Güvenli Alan"a (Cep veya Depo) otonom olarak çekilmesi.
- **Ölçeklenebilir Mimari:** Farklı optimizasyon algoritmalarına izin veren SOLID prensipleri ve Strateji Tasarım Deseni (Strategy Pattern) ile inşa edilmiş yapı.

## 🛠 Teknik Özellikler

- **Dil:** C# / .NET
- **Yol Geometrisi:** 40 metrede bir sensör bulunan 240 metrelik parkur.
- **Güvenlik Mesafesi ($d_{min}$):** Tüm araçlar arasında minimum 20 metre güvenlik.
- **Kaynak Yönetimi:**
  - **Cepler (Pockets):** 1 araç kapasiteli bekleme alanları.
  - **Depolar (Depots):** 3 araç kapasiteli, aynı zamanda güvenli liman (cep) olarak kullanılabilen alanlar.

## 📊 Çalışma Mantığı

Simülasyon "Tick" (zaman adımı) bazlı çalışır. Her döngüde `TrafficController` şunları gerçekleştirir:
1. Araçlar arasındaki mesafeyi değerlendirir.
2. Karşı karşıya gelme veya çarpışma risklerini kontrol eder.
3. Bir kilitlenme (deadlock) algılandığında, düşük öncelikli araca bir **Manevra Görevi** atayarak onu en yakın boş cebe veya depoya gitmesi için yönlendirir.

## 👥 Katkıda Bulunanlar

Bu proje, **Karadeniz Teknik Üniversitesi, Bilgisayar Mühendisliği Bölümü** Optimizasyon dersi kapsamında 2 öğrenci tarafından bir takım halinde geliştirilmiştir.
