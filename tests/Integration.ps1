param([string]$BaseUrl='http://localhost:5038')
$ErrorActionPreference='Stop'
# Fresh CI shells have not loaded the web cmdlets or their session type yet.
Import-Module Microsoft.PowerShell.Utility -ErrorAction Stop
$script:Session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
$script:Headers=@{'X-SpazaSnap'='1'}
function Check($ok,[string]$name) { if(-not $ok){throw "FAILED: $name"}; Write-Output "PASS: $name" }
function Get-Api($path){ $result=Invoke-RestMethod "$BaseUrl/api$path" -WebSession $script:Session -Headers $script:Headers; return $result }
function Post-Api($path,$body){ Invoke-RestMethod "$BaseUrl/api$path" -WebSession $script:Session -Headers $script:Headers -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 8) }
function Expect-Status($path,$body,[int]$status){ try { Post-Api $path $body | Out-Null; throw 'Expected rejection' } catch { if([int]$_.Exception.Response.StatusCode -ne $status){throw}; Write-Output "PASS: $status for $path" } }
Check ((Get-Api '/health').database -eq 'MySQL') 'MySQL health'
try { Invoke-RestMethod "$BaseUrl/api/products" | Out-Null;throw 'Anonymous inventory exposed' } catch { if([int]$_.Exception.Response.StatusCode -ne 401){throw}; Write-Output 'PASS: Anonymous stock access rejected' }
$suffix=[guid]::NewGuid().ToString('N')
$email="owner-$suffix@example.com"
$password='Integration-shop-password-123!'
Expect-Status '/account/register' @{shopName='Weak';email="weak-$suffix@example.com";password='short1'} 400
$account=Post-Api '/account/register' @{shopName='Integration Shop A';email=$email;password=$password;includeSamples=$true}
Check ($account.shopId -gt 0 -and $account.plan -eq 'Free') 'Self-service shop registration'
Check (@(Get-Api '/products').Count -eq 6) 'Optional seed products belong to the new shop'
Expect-Status '/account/register' @{shopName='Duplicate';email=$email.ToUpper();password=$password} 400
Check ((Get-Api '/account/session').shopId -eq $account.shopId) 'Encrypted session identifies the shop'
$ownerSession=$script:Session

$p=Post-Api '/products' @{name="Integration $([guid]::NewGuid().ToString('N').Substring(0,8))";category='Test';unit='each';price=12.50;quantity=8;reorderLevel=10;dailySales=3}
Check ($p.id -gt 0) 'Create product'
Expect-Status '/products' @{name='';category='Test';unit='each';price=1;quantity=0;reorderLevel=0;dailySales=0} 400
Expect-Status "/products/$($p.id)/adjust" @{quantity=-1;reason='Invalid'} 400
Post-Api "/products/$($p.id)/adjust" @{quantity=5;reason='Integration shelf count'} | Out-Null
Check (((Get-Api '/products') | Where-Object id -eq $p.id).quantity -eq 5) 'Count persisted to MySQL'
$r=(Get-Api '/restocks') | Where-Object productId -eq $p.id
Check ($r.suggestedQuantity -eq 16 -and $r.urgency -eq 'Order soon') 'Restock estimate'
Expect-Status '/orders' @{supplier='Test';channel='USSD draft';items=@(@{productId=$p.id;quantity=0})} 400
Expect-Status '/orders' @{supplier='Test';channel='USSD draft';items=@(@{productId=$p.id;quantity=2},@{productId=$p.id;quantity=1})} 400
$before=@(Get-Api '/orders').Count
Expect-Status '/orders' @{supplier='Rollback Test';channel='Online draft';items=@(@{productId=$p.id;quantity=2},@{productId=2147483647;quantity=1})} 400
Check (@(Get-Api '/orders').Count -eq $before) 'Invalid order rolled back completely'
$o=Post-Api '/orders' @{supplier='Integration supplier';channel='USSD draft';items=@(@{productId=$p.id;quantity=7})}
Check (((Get-Api '/orders') | Where-Object id -eq $o.id).status -eq 'Draft') 'USSD is saved as draft, never falsely sent'
Post-Api "/orders/$($o.id)/receive" @{} | Out-Null
Check (((Get-Api '/products') | Where-Object id -eq $p.id).quantity -eq 12) 'Received delivery increases stock'
Expect-Status "/orders/$($o.id)/receive" @{} 400
Check (((Get-Api '/products') | Where-Object id -eq $p.id).quantity -eq 12) 'Duplicate receipt leaves stock unchanged'
$temp=Join-Path ([IO.Path]::GetTempPath()) ("spazasnap-check-"+[guid]::NewGuid().ToString('N')+'.png')
try {
 [IO.File]::WriteAllBytes($temp,[Convert]::FromBase64String('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jRZkAAAAASUVORK5CYII='))
 $photo=Invoke-RestMethod "$BaseUrl/api/photos" -WebSession $script:Session -Headers $script:Headers -Method Post -Form @{photo=Get-Item $temp}
 Check ($photo.status -eq 'manual_review') 'Photo saved for honest manual review'
 $image=Invoke-WebRequest "$BaseUrl/api/photos/$($photo.id)" -WebSession $script:Session
 Check ($image.Headers['Content-Type'] -eq 'image/png') 'Stored photo retrieval'
 Post-Api "/products/$($p.id)/adjust" @{quantity=4;reason='Photo count';photoId=$photo.id} | Out-Null
 Check (((Get-Api '/products') | Where-Object id -eq $p.id).quantity -eq 4) 'Photo-linked count persisted'
 [IO.File]::WriteAllText($temp,'This is not a photo')
 try { Invoke-RestMethod "$BaseUrl/api/photos" -WebSession $script:Session -Headers $script:Headers -Method Post -Form @{photo=Get-Item $temp} | Out-Null; throw 'Expected invalid photo rejection' } catch { if([int]$_.Exception.Response.StatusCode -ne 400){throw}; Write-Output 'PASS: Reject fake photo' }
} finally { Remove-Item -LiteralPath $temp -ErrorAction SilentlyContinue }
try { Invoke-RestMethod "$BaseUrl/api/products" -Method Post -WebSession $script:Session -Headers @{Origin='https://untrusted.example';'X-SpazaSnap'='1'} -ContentType 'application/json' -Body '{}' | Out-Null; throw 'Expected origin rejection' } catch { if([int]$_.Exception.Response.StatusCode -ne 403){throw}; Write-Output 'PASS: Reject cross-origin mutation' }

# A second, independently authenticated shop must not read or mutate Shop A.
$script:Session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
$bEmail="other-$suffix@example.com"
$b=Post-Api '/account/register' @{shopName='Integration Shop B';email=$bEmail;password=$password}
Check (@(Get-Api '/products').Count -eq 0) 'New shop sees no other shop products'
Check (@(Get-Api '/orders').Count -eq 0) 'New shop sees no other shop orders'
Expect-Status "/products/$($p.id)/adjust" @{quantity=999;reason='Foreign count';shopId=$account.shopId} 404
Expect-Status '/orders' @{supplier='Foreign order';channel='Online draft';items=@(@{productId=$p.id;quantity=1})} 400
Expect-Status "/orders/$($o.id)/receive" @{} 404
try { Get-Api "/photos/$($photo.id)" | Out-Null;throw 'Foreign photo exposed' } catch { if([int]$_.Exception.Response.StatusCode -ne 404){throw}; Write-Output 'PASS: Foreign photo rejected' }
$own=Post-Api '/products' @{name='Other shop item';category='Test';unit='each';price=1;quantity=0;reorderLevel=0;dailySales=0;shopId=$account.shopId}
Expect-Status "/products/$($own.id)/adjust" @{quantity=1;reason='Foreign photo';photoId=$photo.id} 400
Check (((Get-Api '/products') | Where-Object id -eq $own.id).quantity -eq 0) 'Foreign photo reference rolls back stock change'
Check ((Get-Api '/account/plan').productsUsed -eq 1) 'Plan usage scoped to current shop'
for($i=2;$i -le 100;$i++) { Post-Api '/products' @{name="Quota test $i";category='Test';unit='each';price=1;quantity=0;reorderLevel=0;dailySales=0} | Out-Null }
Expect-Status '/products' @{name='Over quota';category='Test';unit='each';price=1;quantity=0;reorderLevel=0;dailySales=0} 400
Check ((Get-Api '/account/plan').productsUsed -eq 100) 'Free plan enforces 100-product limit'
for($i=1;$i -le 5;$i++){Expect-Status '/account/login' @{email=$bEmail;password='wrong-password-123'} 401}
Expect-Status '/account/login' @{email=$bEmail;password=$password} 401
Post-Api '/account/logout' @{} | Out-Null
try { Get-Api '/products' | Out-Null;throw 'Logout did not clear session' } catch { if([int]$_.Exception.Response.StatusCode -ne 401){throw}; Write-Output 'PASS: Sign-out removes authenticated access' }
$script:Session=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
$login=Post-Api '/account/login' @{email=$email;password=$password}
Check ($login.shopId -eq $account.shopId) 'Existing owner can sign in again'
Check (((Get-Api '/products') | Where-Object id -eq $p.id).quantity -eq 4) 'Cross-shop attacks left owner stock unchanged'
Check (@(Get-Api '/products').Count -eq 7) 'Quota and products isolated between shops'
$csrfSession=[Microsoft.PowerShell.Commands.WebRequestSession]::new()
$csrfSession.Cookies=$script:Session.Cookies
try { Invoke-RestMethod "$BaseUrl/api/products" -WebSession $csrfSession -Method Post -ContentType 'application/json' -Body '{}' | Out-Null;throw 'Missing CSRF header accepted' } catch { if([int]$_.Exception.Response.StatusCode -ne 403){throw}; Write-Output 'PASS: Missing CSRF header rejected' }
Write-Output 'All SaaS integration checks passed. Tests intentionally leave labelled accounts and records in the selected test database.'

