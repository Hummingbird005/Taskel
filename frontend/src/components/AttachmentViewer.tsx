import { useEffect, useRef, useState, type ReactNode, type SyntheticEvent } from 'react'
import { createPortal } from 'react-dom'
import { attachmentsApi, previewKind, type Attachment } from '../api/attachments'
import { formatBytes } from '../lib/text'
import Icon from './Icon'

interface Props {
  /** The files that can be shown: PDF files and pictures. */
  files: Attachment[]
  /** The one to show first. */
  start: number
  /** Where each file is downloaded from; it is shown from the same address with ?view=true. */
  urlOf: (file: Attachment) => string
  onClose: () => void
}

const ZOOM_STEPS = [0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4]

type Pdf = { state: 'loading' } | { state: 'ready'; url: string } | { state: 'failed' }

/**
 * Shows a task's PDF files and pictures over the task, without saving them: one at a time, with the previous and
 * next, zoom for pictures, and a download button. PDF files open in the browser's own viewer.
 */
export default function AttachmentViewer({ files, start, urlOf, onClose }: Props) {
  const dialog = useRef<HTMLDialogElement>(null)
  const [index, setIndex] = useState(Math.min(start, files.length - 1))
  const [zoom, setZoom] = useState<number | 'fit'>('fit')
  const [imageFailed, setImageFailed] = useState(false)
  const file = files[index]
  const kind = previewKind(file.fileName)
  const viewUrl = attachmentsApi.viewUrl(urlOf(file))

  useEffect(() => {
    const element = dialog.current
    if (element && !element.open) element.showModal()
  }, [])

  function go(step: number) {
    if (files.length < 2) return
    setIndex((i) => (i + step + files.length) % files.length)
    setZoom('fit')
    setImageFailed(false)
  }

  function zoomBy(direction: 1 | -1) {
    setZoom((current) => {
      const now = current === 'fit' ? 1 : current
      const next = direction > 0 ? ZOOM_STEPS.find((s) => s > now) : [...ZOOM_STEPS].reverse().find((s) => s < now)
      return next ?? now
    })
  }

  // The keys work wherever the focus is while the viewer is open: moving to a PDF leaves it nowhere in particular.
  const keys = useRef<(e: KeyboardEvent) => void>(() => {})
  useEffect(() => {
    keys.current = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey) return
      if (e.key === 'ArrowRight') go(1)
      else if (e.key === 'ArrowLeft') go(-1)
      else if (kind === 'image' && (e.key === '+' || e.key === '=')) zoomBy(1)
      else if (kind === 'image' && e.key === '-') zoomBy(-1)
      else if (kind === 'image' && e.key === '0') setZoom('fit')
      else return
      e.preventDefault()
    }
  })
  useEffect(() => {
    const listener = (e: KeyboardEvent) => keys.current(e)
    window.addEventListener('keydown', listener)
    return () => window.removeEventListener('keydown', listener)
  }, [])

  // The viewer is rendered into the page body but belongs to the task in React's tree: its close and cancel events
  // must not reach the task's own dialog, which would close too.
  function stop(e: SyntheticEvent) {
    e.stopPropagation()
  }

  const failed = (
    <div className="viewer-message">
      <Icon name="alert" size={28} />
      <p>Fișierul nu poate fi afișat.</p>
      <a className="button primary" href={urlOf(file)} download={file.fileName}>
        <Icon name="download" />
        Descarcă-l
      </a>
    </div>
  )

  return createPortal(
    <dialog
      ref={dialog}
      className="viewer"
      aria-label={`Vizualizare: ${file.fileName}`}
      onCancel={stop}
      onClose={(e) => {
        stop(e)
        onClose()
      }}
    >
      <header className="viewer-bar">
        <div className="viewer-title">
          <strong title={file.fileName}>{file.fileName}</strong>
          <span>
            {formatBytes(file.size)}
            {files.length > 1 && ` · ${index + 1} din ${files.length}`}
          </span>
        </div>
        <div className="viewer-tools">
          {kind === 'image' && !imageFailed && (
            <div className="viewer-zoom" role="group" aria-label="Mărire">
              <button type="button" onClick={() => zoomBy(-1)} aria-label="Micșorează" title="Micșorează (−)">
                <Icon name="minus" />
              </button>
              <button type="button" className="viewer-zoom-value" onClick={() => setZoom('fit')} title="Potrivește în fereastră (0)">
                {zoom === 'fit' ? 'Potrivit' : `${Math.round(zoom * 100)}%`}
              </button>
              <button type="button" onClick={() => zoomBy(1)} aria-label="Mărește" title="Mărește (+)">
                <Icon name="plus" />
              </button>
            </div>
          )}
          <a className="viewer-button" href={urlOf(file)} download={file.fileName} title="Descarcă" aria-label="Descarcă">
            <Icon name="download" />
          </a>
          <button type="button" className="viewer-button" onClick={() => dialog.current?.close()} title="Închide (Esc)" aria-label="Închide">
            <Icon name="close" />
          </button>
        </div>
      </header>

      <div className={`viewer-stage ${kind === 'image' && zoom !== 'fit' ? 'zoomed' : ''}`}>
        {kind === 'image' &&
          (imageFailed ? (
            failed
          ) : (
            <img
              key={file.id}
              src={viewUrl}
              alt={file.fileName}
              className={zoom === 'fit' ? 'fit' : ''}
              style={zoom === 'fit' ? undefined : { width: `calc(var(--natural-width) * ${zoom})` }}
              onLoad={(e) => e.currentTarget.style.setProperty('--natural-width', `${e.currentTarget.naturalWidth}px`)}
              onError={() => setImageFailed(true)}
              onClick={() => setZoom((z) => (z === 'fit' ? 1 : 'fit'))}
            />
          ))}
        {kind === 'pdf' && <PdfFrame key={file.id} url={viewUrl} title={file.fileName} failed={failed} />}
      </div>

      {files.length > 1 && (
        <>
          <button type="button" className="viewer-nav previous" onClick={() => go(-1)} aria-label="Fișierul anterior" title="Anterior (←)">
            <Icon name="arrowLeft" size={20} />
          </button>
          <button type="button" className="viewer-nav next" onClick={() => go(1)} aria-label="Fișierul următor" title="Următor (→)">
            <Icon name="arrowRight" size={20} />
          </button>
        </>
      )}
    </dialog>,
    document.body,
  )
}

/**
 * A PDF in the browser's own viewer. It is fetched first, so a file the server will not show is reported here
 * rather than as an error page in the frame; it is kept in memory only, as a blob, and nothing is saved.
 */
function PdfFrame({ url, title, failed }: { url: string; title: string; failed: ReactNode }) {
  const [pdf, setPdf] = useState<Pdf>({ state: 'loading' })

  useEffect(() => {
    let cancelled = false
    let blobUrl: string | null = null
    fetch(url, { credentials: 'include' })
      .then(async (res) => {
        if (!res.ok) throw new Error(String(res.status))
        const blob = await res.blob()
        if (cancelled) return
        blobUrl = URL.createObjectURL(new Blob([blob], { type: 'application/pdf' }))
        setPdf({ state: 'ready', url: blobUrl })
      })
      .catch(() => {
        if (!cancelled) setPdf({ state: 'failed' })
      })
    return () => {
      cancelled = true
      if (blobUrl) URL.revokeObjectURL(blobUrl)
    }
  }, [url])

  if (pdf.state === 'failed') return failed
  if (pdf.state === 'ready') return <iframe src={pdf.url} title={title} />
  return (
    <div className="viewer-message">
      <span className="spinner" aria-hidden="true" />
      <p>Se deschide documentul…</p>
    </div>
  )
}
