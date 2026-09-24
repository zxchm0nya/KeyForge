# KeyForge

Нативное приложение для настройки клавиатур через VIA-протокол с поддержкой JSON-конфигураций без использования браузера.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)]()
[![Release](https://img.shields.io/github/v/release/zxchm0nya/KeyForge)]()

## Особенности

- Нативное приложение, работающее быстрее браузерной версии
- Поддержка JSON-конфигураций для загрузки пользовательских раскладок
- Автоматическое определение клавиатуры при подключении
- Фон: картинки, GIF и видео; плавный скролл; перетаскивание блоков
- Проверка обновлений с GitHub Releases

## Скриншоты

![Main Interface](screenshots/1.png)
*Основной интерфейс настройки*

![Key Mapping](screenshots/2.png)
*Назначение клавиш*

![Lighting](screenshots/3.png)
*Настройка подсветки*

![Macros](screenshots/4.png)
*Редактор макросов*

## Быстрый старт

Готовый установщик — в разделе [Releases](https://github.com/zxchm0nya/KeyForge/releases).

### Сборка из исходников

```bash
git clone https://github.com/zxchm0nya/KeyForge.git
cd KeyForge

# Сборка приложения
dotnet build KyeForge.sln -c Release

# Или полный релиз (app + installer)
powershell -ExecutionPolicy Bypass -File build.ps1
```

## Структура

- `KyeForge.App` — основное приложение (WPF)
- `KyeForge.Installer` — установщик
- `build.ps1` — чистка, сборка, упаковка setup
