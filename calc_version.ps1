$desc=$(git describe)
$d1=$desc -split '-'
if($d1.Count -lt 3) {
  return
}
$d2=$d1[0] -split "\+"
if($d2.Count -lt 2) {
  return
}
$d3=$d2[0] -split "\."
$hash=$d1[2]
$incr=$d1[1]
$maj=$d3[0]
$min=$d3[1]
$lev=$d3[2]
$new_lev=[int]$lev+[int]$incr
$semver="$maj.$min.$new_lev+$hash"
Write-Output "New version is: $semver"
git tag -a $semver -m "bump release version to $semver"

