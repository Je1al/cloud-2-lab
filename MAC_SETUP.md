# Запуск лабораторной работы на macOS

## Что уже подготовлено

В `SPS.Student/ProfitSystemPolicy.cs` находится итоговая стратегия `ProfitSystemPolicy`, реализующая `ISystemPolicy`. Она подключена в `SPS.Runner/Program.cs`. Движок симуляции и абстракции не изменялись.

Стратегия использует кэш и объединение одинаковых загрузок, балансировку API, приоритизацию Generate-задач по доходности, восстановление зависших воркеров, адаптивное масштабирование и ограниченные повторы сетевых RPC.

## Установка

Проект использует `net8.0`. Установленный у тебя SDK `6.0.136` для него не подходит, поэтому команда `dotnet build` воспринимается как попытка запустить приложение `build`.

Самый простой вариант из каталога проекта:

```bash
chmod +x install-dotnet8-mac.sh run-mac.sh
./install-dotnet8-mac.sh
```

Скрипт устанавливает официальный .NET SDK 8 в `~/.dotnet` и сам определяет архитектуру Mac. Альтернативно можно установить SDK 8 с официальной страницы Microsoft: <https://dotnet.microsoft.com/download/dotnet/8.0>. Для Apple Silicon выбирай **Arm64**, для Intel — **x64**.

Проверь установку:

```bash
dotnet --version
```

Должна выводиться версия `8.0.x`. Если Terminal всё ещё показывает `6.0.136`, выполни:

```bash
export PATH="$HOME/.dotnet:$PATH"
dotnet --version
```

Чтобы настройка сохранялась после перезапуска Terminal:

```bash
echo 'export PATH="$HOME/.dotnet:$PATH"' >> ~/.zshrc
source ~/.zshrc
```

## Запуск

Перейди в каталог проекта и выполни:

```bash
cd /путь/к/SPSPublic
dotnet build SPS.sln
dotnet run --project SPS.Runner -- --seed 42
```

Для другого прогона измени seed:

```bash
dotnet run --project SPS.Runner -- --seed 2026
```

Для трассировки первых запросов:

```bash
dotnet run --project SPS.Runner -- --seed 42 --trace 10
```

Для записи полной телеметрии:

```bash
mkdir -p output
dotnet run --project SPS.Runner -- --seed 42 --record output/trace.jsonl
```

## Как сдавать

Для сдачи достаточно файла `SPS.Student/ProfitSystemPolicy.cs`. Он не использует внешние NuGet-пакеты и зависит только от `SPS.Abstractions`, как требует условие. Если преподавателю нужен весь проект, отправляй весь каталог `SPSPublic`, исключив папки `bin` и `obj`.

## Проверка перед отправкой

```bash
dotnet clean SPS.sln
dotnet build SPS.sln --nologo
dotnet run --project SPS.Runner --no-build -- --seed 42
```

В выводе должны быть строки `Profit`, `Revenue`, `API cost`, `Worker cost` и метрики по `Metadata`, `Preview`, `Generate`. Значения Profit зависят от seed и открытого сценария.
