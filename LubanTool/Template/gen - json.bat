set WORKSPACE=..
set LUBAN_DLL=%WORKSPACE%\Tools\Luban\Luban.dll
set CONF_ROOT=.

dotnet %LUBAN_DLL% ^
    -t all ^
    -c cs-newtonsoft-json ^
    -d json  ^
    --conf %CONF_ROOT%\luban.conf ^
    -x outputCodeDir=..\..\Assets\CodePatch\Core\Config\Generate^
    -x outputDataDir=..\..\Assets\yoo\DefaultPackage\Config

pause