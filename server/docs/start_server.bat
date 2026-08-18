@echo off
echo ===================================================
echo     Запуск локального сервера NEXUS Dashboard
echo ===================================================
echo.
echo Этот скрипт запустит локальный сервер, чтобы избежать 
echo блокировки CORS политикой браузера.
echo.

python -m http.server 8000
if %errorlevel% neq 0 (
    echo.
    echo ОШИБКА: Python не найден. Попытка запустить через npx...
    npx serve -l 8000
    if %errorlevel% neq 0 (
        echo.
        echo ОШИБКА: Сервер не запущен. Пожалуйста, установите Python или NodeJS.
    )
)

pause
