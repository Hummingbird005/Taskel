// Builds the PDF manuals from src/*.html: node docs/manuale/build.mjs
// Needs Playwright with Chromium (npx playwright install chromium) and the Inter font for the intended look.
import { createRequire } from 'node:module'
import { readdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs'
import { dirname, join } from 'node:path'
import { fileURLToPath, pathToFileURL } from 'node:url'

const require = createRequire(import.meta.url)
const { chromium } = require('playwright')
const here = dirname(fileURLToPath(import.meta.url))
const src = join(here, 'src')

const page = (title, body) => `<!doctype html>
<html lang="ro"><head><meta charset="utf-8"><title>${title}</title>
<link rel="stylesheet" href="../manual.css"></head><body>${body}</body></html>`

const browser = await chromium.launch()
for (const file of readdirSync(src).filter((f) => f.endsWith('.html') && !f.startsWith('_'))) {
  const body = readFileSync(join(src, file), 'utf8')
  const title = /<h1>(.*?)<\/h1>/s.exec(body)?.[1].replace(/<[^>]+>/g, '') ?? 'Taskel'
  const tmp = join(src, `_${file}`)
  writeFileSync(tmp, page(title, body))
  const tab = await browser.newPage()
  await tab.goto(pathToFileURL(tmp).href, { waitUntil: 'load' })
  // Number the chapters and figures, and fill in the table of contents.
  const broken = await tab.evaluate(() => {
    const toc = document.querySelector('.toc ol')
    let chapter = 0
    let figure = 0
    for (const el of document.querySelectorAll('h2:not(.plain), h3, figcaption')) {
      if (el.tagName === 'H2') {
        chapter += 1
        el.id = `c${chapter}`
        el.innerHTML = `<span class="num">${chapter}</span>${el.innerHTML}`
        toc?.insertAdjacentHTML('beforeend', `<li><span class="n">${chapter}</span><a href="#${el.id}">${el.textContent.replace(/^\d+/, '')}</a></li>`)
      } else if (el.tagName === 'H3' && chapter > 0 && !el.closest('.toc')) {
        el.id = `s${chapter}-${toc?.children.length}`
        toc?.insertAdjacentHTML('beforeend', `<li class="sub"><a href="#${el.id}">${el.textContent}</a></li>`)
      } else if (el.tagName === 'FIGCAPTION') {
        figure += 1
        el.innerHTML = `<b>Figura ${figure}.</b> ${el.innerHTML}`
      }
    }
    return [...document.images].filter((img) => !img.complete || img.naturalWidth === 0).map((img) => img.getAttribute('src'))
  })
  if (broken.length > 0) throw new Error(`${file}: missing images ${broken.join(', ')}`)
  const out = join(here, file.replace('.html', '.pdf'))
  await tab.pdf({
    path: out,
    format: 'A4',
    printBackground: true,
    preferCSSPageSize: true,
    displayHeaderFooter: true,
    headerTemplate: '<span></span>',
    footerTemplate: `<div style="width:100%;font-family:Inter,Arial,sans-serif;font-size:7.5pt;color:#7a8394;padding:0 17mm;display:flex;justify-content:space-between"><span>Taskel · ${title}</span><span class="pageNumber"></span></div>`,
  })
  await tab.close()
  rmSync(tmp)
  console.log('built', out)
}
await browser.close()
