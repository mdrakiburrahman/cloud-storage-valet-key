# Parameters
# ----------
param ($uncServer, `                                    # UNC Server FQDN or Private IP
      $fileshare, `                                     # Fileshare path
      $storageContainerName, `                          # Azure Storage Container
      $storageAccountRG, `                              # Azure Resource Group containing Storage Account
      $storageAccountName, `                            # Storage Account name
      $clientSecret, `                                  # Secret: AAD Service principal with access to Storage Account
      $appId, `                                         # Client ID: AAD Service principal with access to Storage Account
      $tenantId, `                                      # AAD Tenant ID
      $functionURL, `                                   # Azure Function URL to call for SAS URL
      $onPremAcctUsername, `                            # On Prem Service Account with access to File Share
      $onPremAcctPassword)                              # On Prem Service Account password access to File Share

# Generate UNC Path
$UNCPath = "\\$uncServer\$fileshare"

# Service Principal with access to Storage Account for Upload
$env:AZCOPY_SPA_CLIENT_SECRET="$($clientSecret)"

# Main
# ----------
# Authenticate to Azure via Service Principal
azcopy login --service-principal  --application-id=$appId --tenant-id=$tenantId

# Get Storage Account Context and generate URI
$storageAccountKey = (Get-AzStorageAccountKey -ResourceGroupName $storageAccountRG -AccountName $storageAccountName).Value[0]
$destinationContext = New-AzStorageContext -StorageAccountName $storageAccountName -StorageAccountKey $storageAccountKey
$containerSASURI = New-AzStorageContainerSASToken -Context $destinationContext -ExpiryTime(get-date).AddSeconds(36000) -FullUri -Name $storageContainerName -Permission rwdl

Write-Host $containerSASURI

Write-Host -ForegroundColor Yellow "`n Performing azcopy sync - one way..`n"    

PsExec -i -u $onPremAcctUsername -p $onPremAcctPassword azcopy sync $UNCPath $containerSASURI

Write-Host -ForegroundColor Green "Sync complete."

# Leverage Service Account creds for populating files from File Share
net use "\\$uncServer" $onPremAcctPassword /USER:$onPremAcctUsername 

# Grab list of files (exclude folders) from UNC Path
$fileList = Get-ChildItem -Path $UNCPath -Attributes !Directory -Recurse -Force

# Print out SAS URLs per file
Write-Host -ForegroundColor Yellow "`n Generating SAS link via Azure Function call..`n"    

$fileList.ForEach(
    {
        Write-Host 
        
        # Generate prefix for nested files for our SAS URL
        $Directory = $_.Directory
		$DirArray = $Directory.ToString().Split("\")
        $prefix = ""
        for ($i=1; $i -le ($DirArray.Count - 4); $i++) 
            {
                $prefix += $DirArray[$i+3] + "/"
            }

        # Create the body of the webrequest
        $body = '{"container":"' + $storageContainerName +'","blobName":"' + $prefix + $_.Name + '", "permissions":"Read", "time": 1}' 

        # Invoke function and submit the JSON body
        $response = Invoke-WebRequest $functionURL  -Method Post -Body $body -ContentType 'application/json'

        # Print SAS URL
        $sasURL = $response.Content

        Write-Color -Text ($_.Name + ": "), $sasURL -Color Blue,White
    }
)

Write-Host -ForegroundColor Green "`n SAS Generation complete. `n"