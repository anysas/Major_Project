$path = "Assets\Straight_imports\compactor.fbx"
$b = [IO.File]::ReadAllBytes($path)
$s = [Text.Encoding]::ASCII.GetString($b)
$matches = [regex]::Matches($s, "[A-Za-z][A-Za-z0-9_\-]{2,40}")
$set = @{}
foreach ($m in $matches) {
  $v = $m.Value
  if ($v -match 'arm|boom|blade|lift|hydra|bucket|piston|link|joint|hinge|dump|bed|body|cabin|frame|chassis|roller|wheel|cab') {
    $set[$v] = $true
  }
}
$set.Keys | Sort-Object
