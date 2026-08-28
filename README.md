# KeyForge

Нативное приложение для настройки клавиатур через VIA-протокол с поддержкой JSON-конфигураций без использования браузера.

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![Platform](https://img.shields.io/badge/platform-Windows%20%7C%20macOS%20%7C%20Linux-lightgrey)]()
[![Release](https://img.shields.io/github/v/release/yourusername/keyforge)]()

## Особенности

- Нативное приложение, работающее быстрее браузерной версии
- Поддержка JSON-конфигураций для загрузки пользовательских раскладок
- Автоматическое определение клавиатуры при подключении
- Сохранение и быстрое переключение профилей
- Кроссплатформенность: Windows, macOS, Linux

## Скриншоты

![Main Interface](docs/screenshots/main.png)
*Основной интерфейс настройки*

## Быстрый старт

В последних обновлениях доступен готовый установщик. Достаточно скачать его из раздела [Releases](https://github.com/yourusername/keyforge/releases), установить и использовать приложение без консоли и дополнительных действий.

### Сборка из исходников

```bash
git clone https://github.com/yourusername/keyforge.git
cd keyforge

npm install
npm start

# Сборка для вашей ОС
npm run build