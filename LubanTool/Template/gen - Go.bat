set WORKSPACE=..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\Luban.dll
set CONF_ROOT=.

dotnet %LUBAN_DLL% ^
    -t server ^
    -c go-json ^
    -d json  ^
    --conf %CONF_ROOT%\luban.conf ^
    -x outputCodeDir=GoGen\code-generate ^
    -x outputDataDir=GoGen\json-data ^
    -x lubanGoModule=demo/luban
pause