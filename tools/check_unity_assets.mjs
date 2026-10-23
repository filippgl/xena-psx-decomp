#!/usr/bin/env node
import fs from 'node:fs';
import path from 'node:path';
import { spawnSync } from 'node:child_process';
const home = path.resolve('build/dotnet-home'); fs.mkdirSync(home, { recursive: true });
const env = { ...process.env, DOTNET_CLI_HOME: home, DOTNET_SKIP_FIRST_TIME_EXPERIENCE: '1',
  DOTNET_CLI_TELEMETRY_OPTOUT: '1', DOTNET_GENERATE_ASPNET_CERTIFICATE: 'false', NUGET_PACKAGES: path.resolve('build/nuget-packages') };
const project = 'tools/UnityAssetCheck/UnityAssetCheck.csproj';
for (const args of [['restore', project, '--configfile', 'tools/UnityAssetCheck/NuGet.Config', '--verbosity', 'quiet'],
  ['build', project, '--no-restore', '--nologo', '--verbosity', 'quiet'],
  ['build/unity-check/bin/Debug/net9.0/UnityAssetCheck.dll']]) {
  const result = spawnSync('dotnet', args, { env, stdio: 'inherit', windowsHide: true });
  if (result.error || result.status !== 0) throw Error(`.NET check failed: ${result.error?.message || result.status}`);
}
