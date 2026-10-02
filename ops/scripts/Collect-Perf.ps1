<# Windows perf capture for a slow SupplyMesh API host. Usage: .\Collect-Perf.ps1 -Minutes 5 -Out C:\perf #>
param([int]$Minutes = 5, [string]$Out = "C:\perf")
New-Item -ItemType Directory -Force $Out | Out-Null
$counters = @(
  '\Processor(_Total)\% Processor Time', '\Memory\Available MBytes', '\Memory\Pages/sec',
  '\Process(dotnet*)\% Processor Time', '\Process(dotnet*)\Working Set', '\.NET CLR Memory(*)\% Time in GC',
  '\.NET CLR Exceptions(*)\# of Exceps Thrown / sec', '\ASP.NET Core Hosting\Requests Per Second',
  '\PhysicalDisk(_Total)\Avg. Disk sec/Read', '\Network Interface(*)\Bytes Total/sec', '\TCPv4\Segments Retransmitted/sec',
  '\SQLServer:SQL Statistics\Batch Requests/sec', '\SQLServer:Locks(_Total)\Lock Waits/sec', '\SQLServer:Buffer Manager\Page life expectancy')
Get-Counter -Counter $counters -SampleInterval 5 -MaxSamples ($Minutes * 12) |
  Export-Counter -Path "$Out\perf_$(Get-Date -f yyyyMMdd_HHmm).blg" -FileFormat BLG
Get-WinEvent -FilterHashtable @{LogName='Application'; Level=1,2; StartTime=(Get-Date).AddHours(-1)} | Export-Csv "$Out\app_errors.csv" -NoTypeInformation
netstat -ano | Select-String ESTABLISHED | Measure-Object | ForEach-Object { "Established TCP connections: $($_.Count)" } | Tee-Object "$Out\netstat_summary.txt"
Write-Host "Done. Open .blg in PerfMon (perfmon /sys) to analyse."
