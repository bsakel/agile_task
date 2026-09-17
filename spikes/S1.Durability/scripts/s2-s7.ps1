$ErrorActionPreference = 'Continue'
. { # reuse helpers
  $exe = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability\bin\Debug\net10.0\S1.Durability.exe'
  $wd  = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability'
}
$exe = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability\bin\Debug\net10.0\S1.Durability.exe'
$wd  = 'C:\Users\bsakel\source\repos\agile_task\spikes\S1.Durability'
$url = 'http://localhost:5099'
$logDir = $PSScriptRoot
function Psql($sql) { docker exec spike-pg psql -U postgres -d spike -t -A -c "set client_min_messages to warning; $sql" }

Psql "drop schema if exists s1 cascade; drop schema if exists wolverine cascade; drop schema if exists s2 cascade;" | Out-Null
$p = Start-Process -FilePath $exe -ArgumentList "--urls $url" -WorkingDirectory $wd -PassThru -NoNewWindow `
     -RedirectStandardOutput "$logDir\s2s7.out.log" -RedirectStandardError "$logDir\s2s7.err.log"
for ($i = 0; $i -lt 60; $i++) { try { Invoke-RestMethod "$url/status" -TimeoutSec 5 | Out-Null; break } catch { Start-Sleep -Milliseconds 500 } }

"=== S2: Dapper transaction + outbox"
$c = Invoke-RestMethod -Method Post "$url/s2/commit"
$r = Invoke-RestMethod -Method Post "$url/s2/rollback"
"message storage implementation: $($c.storage)"
Start-Sleep -Seconds 3
$s = Invoke-RestMethod "$url/status"
"commit id $($c.id): processed=" + (Psql "select count(*) from s1.mt_doc_processed where id = '$($c.id)'") + " row=" + (Psql "select count(*) from s2.items where id = '$($c.id)'")
"rollback id $($r.id): processed=" + (Psql "select count(*) from s1.mt_doc_processed where id = '$($r.id)'") + " row=" + (Psql "select count(*) from s2.items where id = '$($r.id)'")
"outgoing/incoming leftovers: " + (Psql "select 'in:'||status||'='||count(*) from wolverine.wolverine_incoming_envelopes where message_type like '%DapperWork%' group by status") -join '; '

"=== S7: circuit breaker on local queue"
Invoke-RestMethod -Method Post "$url/s7/provider/down" | Out-Null
Invoke-RestMethod -Method Post "$url/s7/publish/30" | Out-Null
Start-Sleep -Seconds 8
$a1 = (Invoke-RestMethod "$url/status").Attempts
Start-Sleep -Seconds 6
$a2 = (Invoke-RestMethod "$url/status").Attempts
"attempts after 8s=$a1, after 14s=$a2 (flat => listener paused)"
Invoke-RestMethod -Method Post "$url/s7/provider/up" | Out-Null
Start-Sleep -Seconds 25
$s = Invoke-RestMethod "$url/status"
"after provider up + pause time: flaky processed=$($s.processed.flaky) attempts=$($s.Attempts)"
"dead letters: " + (Psql "select message_type||'='||count(*) from wolverine.wolverine_dead_letters group by message_type") -join '; '
"incoming FlakyWork by status: " + (Psql "select status||'='||count(*) from wolverine.wolverine_incoming_envelopes where message_type like '%FlakyWork%' group by status") -join '; '
Stop-Process -Id $p.Id -Force
"--- breaker log lines:"
Select-String -Path "$logDir\s2s7.out.log" -Pattern 'ircuit|paus|resum' | Select-Object -First 8 | ForEach-Object { $_.Line.Trim() }
