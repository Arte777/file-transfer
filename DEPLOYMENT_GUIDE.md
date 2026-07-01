# Инструкция по обновлению и деплою RAH (v7.2.2+)

Эта инструкция предназначена для разработчиков и ИИ-ассистентов, которые будут заниматься выпуском новых версий.

---

## Шаг 1. Обновление версии в исходном коде

Перед сборкой необходимо обновить строковые константы версии в трех местах:
1. **WPF-клиент (RAH Non Pro):**
   В файле `client-wpf/MainWindow.xaml.cs` найдите и обновите:
   ```csharp
   private static string AppTitleVersionText = " vX.Y.Z";
   private static string WindowTitleText = "RAH NonPro vX.Y.Z";
   private static string ClientVersion = "X.Y.Z";
   ```
2. **WPF-клиент (RAH PRO Standalone):**
   В файле `standalone-shonll/MainWindow.xaml.cs` найдите и обновите аналогичные переменные.
3. **Бэкенд-сервер:**
   В файле `server/server.js` обновите актуальную версию клиента:
   ```javascript
   const CURRENT_CLIENT_VERSION = 'X.Y.Z';
   ```

---

## Шаг 2. Сборка фонового клона (Update Payload)

Фоновое обновление запущенных копий (клонов) на компьютерах пользователей скачивает исполняемый файл напрямую с сервера. Этот файл должен называться `Runtime Broker.exe` внутри сборки, но на сервере он лежит под именем `RAH_Non_Pro.exe`.

1. Очистите старые артефакты и запустите сборку с подменой имени выходного файла (`AssemblyName`):
   ```powershell
   dotnet publish client-wpf/FileTransfer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:AssemblyName="Runtime Broker" -o compiled_clients\vX.Y.Z\clone-sc
   ```
2. Скопируйте полученный файл в директорию загрузок веб-панели под именем `RAH_Non_Pro.exe`:
   ```powershell
   Copy-Item -Path "compiled_clients\vX.Y.Z\clone-sc\Runtime Broker.exe" -Destination "docs\downloads\RAH_Non_Pro.exe" -Force
   ```

---

## Шаг 3. Сборка WPF-клиентов для установщиков

Для создания установщиков нам нужны обычные сборки приложений (с оригинальными именами файлов).

1. Соберите клиент `RAH Non Pro` (с исходным именем сборки):
   ```powershell
   dotnet publish client-wpf/FileTransfer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o C:\temp\ft-build-shonll-8
   ```
2. Соберите клиент `RAH PRO` (с исходным именем сборки):
   ```powershell
   dotnet publish standalone-shonll/FileTransfer.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o standalone-shonll\publish
   ```

*Примечание: Тип сборки для иконки `app.ico` в файлах проектов обязательно должен быть `Resource` (а не `None`), а сама иконка загружается в окнах программно через `pack://application:,,,/app.ico` во избежание ошибок XAML при запуске.*

---

## Шаг 4. Обновление и сборка инсталляторов (Inno Setup)

1. Откройте файлы скриптов `setup/shonll_client.iss` и `setup/shonll_standalone.iss` и измените макрос версии:
   ```pascal
   #define MyAppVersion "X.Y.Z"
   ```
2. Скомпилируйте установщики с помощью утилиты командной строки Inno Setup:
   ```powershell
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup\shonll_client.iss
   & "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" setup\shonll_standalone.iss
   ```
3. Скопируйте готовые дистрибутивы в папку загрузок веб-панели:
   * Для клиента:
     ```powershell
     Copy-Item -Path "C:\Users\user\Documents\RAH SETUPS\RAH_Non_Pro_setup.exe" -Destination "docs\downloads\RAH_Non_Pro_setup.exe" -Force
     ```
   * Для standalone:
     ```powershell
     Copy-Item -Path "C:\Users\user\Documents\RAH_PRO_standalone_setup.exe" -Destination "docs\downloads\RAH_PRO_setup.exe" -Force
     ```

---

## Шаг 5. Коммит изменений и деплой на Railway

Чтобы новые файлы появились на сайте, а бэкенд применил новые версии и логику:

1. Отправьте все измененные файлы и новые бинарники установщиков в репозиторий GitHub:
   ```powershell
   git add -A
   git commit -m "Release vX.Y.Z"
   git push origin master
   ```
2. Бэкенд на Railway автоматически запустит деплой при получении свежего коммита в ветку `master`. Проверить статус сборки можно командой:
   ```powershell
   railway deployment list
   ```

---
Инструкция актуальна для версий RAH 7.2.2 и выше.
