rem please read https://github.com/tooll3/t3/wiki/StandAloneBuilds

@RD /S /Q "..\TiXL-Standalone"
mkdir "..\TiXL-Standalone"

Xcopy "Resources" "..\TiXL-Standalone\Resources" /E /H /C /I
Xcopy ".Variations" "..\TiXL-Standalone\.Variations" /E /H /C /I
Xcopy "Player\bin\Release\net9.0-windows" "..\TiXL-Standalone\" /E /H /C /I
Xcopy "Editor\bin\Release\net9.0-windows" "..\TiXL-Standalone\" /E /H /C /I