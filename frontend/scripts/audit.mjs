// Runs `npm audit` and fails on any high or critical advisory, except those listed below.
//
// Each exception names an advisory that has no fixed release yet and does not reach the served
// application. Remove it as soon as a fixed version exists: update the lockfile (and offline/npm)
// instead.
import { execFileSync } from 'node:child_process'

const ALLOWED = new Map([
  // picomatch <= 4.0.7: ReDoS with repeated extglobs. Used only by the build tools (vite, tinyglobby,
  // fdir) on the project's own glob patterns; not in the bundle. No fixed version as of 2026-11-18.
  ['GHSA-fc5q-39xc-vjpf', 'picomatch'],
])
const BLOCKING = new Set(['high', 'critical'])

let output
try {
  output = execFileSync('npm', ['audit', '--json'], { encoding: 'utf8', maxBuffer: 64 * 1024 * 1024 })
} catch (error) {
  // npm audit exits with 1 when it finds anything; the report is still on stdout.
  output = error.stdout
  if (!output) throw error
}

const report = JSON.parse(output)
const advisories = new Map()
for (const entry of Object.values(report.vulnerabilities ?? {})) {
  // Entries that only depend on a vulnerable package list its name; the advisory itself is an object.
  for (const via of entry.via) {
    if (typeof via === 'object') advisories.set(via.url, via)
  }
}

let failed = false
for (const advisory of advisories.values()) {
  const id = advisory.url.split('/').pop()
  if (!BLOCKING.has(advisory.severity)) continue
  if (ALLOWED.get(id) === advisory.name) {
    console.log(`allowed for now: ${id} ${advisory.name} (${advisory.severity}) ${advisory.title}`)
    continue
  }
  console.error(`${advisory.severity}: ${id} ${advisory.name} ${advisory.range}: ${advisory.title}`)
  failed = true
}
for (const [id, name] of ALLOWED) {
  if (![...advisories.values()].some((a) => a.url.endsWith(id))) {
    console.log(`exception no longer needed, remove it: ${id} (${name})`)
  }
}
if (failed) process.exit(1)
console.log('No blocking advisories.')
