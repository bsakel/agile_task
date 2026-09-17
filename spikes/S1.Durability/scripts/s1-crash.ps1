$ErrorActionPreference = 'Continue'
$exe = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability\bin\Debug\net10.0\S1.Durability.exe'
$wd  = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability'
$url = 'http://localhost:5099'
$logDir = $PSScriptRoot

function Start-App($name) {
  $p = Start-Process -FilePath $exe -ArgumentList "--urls $url" -WorkingDirectory $wd -PassThru -NoNewWindow `
       -RedirectStandardOutput "$logDir\$name.out.log" -RedirectStandardError "$logDir\$name.err.log"
  for ($i = 0; $i -lt 60; $i++) {
    try { Invoke-RestMethod "$url/status" -TimeoutSec 5 | Out-Null; return $p } catch {
      if ($p.HasExited) { throw "app exited during startup, see $name logs" }
      Start-Sleep -Milliseconds 500 }
  }
  Stop-Process -Id $p.Id -Force
  throw "app did not start"
}
function Psql($sql) { docker exec spike-pg psql -U postgres -d spike -t -A -c "set client_min_messages to warning; $sql" }

Psql "drop schema if exists s1 cascade; drop schema if exists wolverine cascade; drop schema if exists s2 cascade;" | Out-Null

$p = Start-App 'run1'
Invoke-RestMethod -Method Post "$url/s1/publish/40" | Out-Null
Invoke-RestMethod -Method Post "$url/s1/outbox/40" | Out-Null
Invoke-RestMethod -Method Post "$url/s1/schedule/20" | Out-Null
Start-Sleep -Milliseconds 1200
Stop-Process -Id $p.Id -Force
Start-Sleep -Seconds 1
"--- after kill (process forcibly terminated)"
"processed docs by kind: " + ((Psql "select data->>'Kind' as kind, count(*) from s1.mt_doc_processed group by 1") -join '; ')
"wolverine incoming by status: " + ((Psql "select status, count(*) from wolverine.wolverine_incoming_envelopes group by 1") -join '; ')

Start-Sleep -Seconds 22
$p = Start-App 'run2'
$deadline = (Get-Date).AddSeconds(90)
do {
  Start-Sleep -Seconds 3
  $s = Invoke-RestMethod "$url/status"
  $slow = [int]($s.processed.slow); $sched = [int]($s.processed.scheduled)
} while ((($slow -lt 80) -or ($sched -lt 1)) -and (Get-Date) -lt $deadline)
"--- after restart"
"processed: slow=$slow scheduled=$sched"
"wolverine incoming by status: " + ((Psql "select status, count(*) from wolverine.wolverine_incoming_envelopes group by 1") -join '; ')
"duplicates (processed ids stored twice are impossible as docs are keyed by id; count distinct): " + (Psql "select count(distinct id) from s1.mt_doc_processed")
Stop-Process -Id $p.Id -Force
