# Скрипт для очистки и запуска тестов
$testProject = "Tests/iikoServiceHelper.Tests/iikoServiceHelper.Tests.csproj"

Write-Host "Cleaning ALL build artifacts..." -ForegroundColor Yellow
Remove-Item -Recurse -Force "Tests/iikoServiceHelper.Tests/bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "Tests/iikoServiceHelper.Tests/obj" -ErrorAction SilentlyContinue
# Clean root project artifacts too because referencing WinExe can be tricky with stale cache
Remove-Item -Recurse -Force "bin" -ErrorAction SilentlyContinue
Remove-Item -Recurse -Force "obj" -ErrorAction SilentlyContinue

Write-Host "Restoring packages..." -ForegroundColor Yellow
dotnet restore $testProject
if ($LASTEXITCODE -ne 0) { 
    Write-Host "Restore failed!" -ForegroundColor Red
    exit $LASTEXITCODE 
}

Write-Host "Running tests..." -ForegroundColor Green
dotnet test $testProject --no-restore --collect:"XPlat Code Coverage"
