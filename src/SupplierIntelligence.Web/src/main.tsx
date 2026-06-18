import { StrictMode, useEffect, useMemo, useState } from 'react'
import { createRoot } from 'react-dom/client'
import {
  archiveSupplier,
  clearOpenRouterApiKey,
  confirmSupplierMatchCandidate,
  createSupplier,
  deleteSourceCheck,
  getSupplierAnalytics,
  getSupplierAnalysisJob,
  getSupplierAnalysisJobs,
  getSupplierConnections,
  getSupplierMatchCandidates,
  getLocalModelStatus,
  getSupplier,
  getSupplierReviewSummary,
  getSuppliers,
  queueSupplierAnalysis,
  recheckOpenQuestions,
  rejectSupplierMatchCandidate,
  researchWebsiteSource,
  saveOpenRouterApiKey,
  suggestSupplierMatchCandidates,
  updateSourceCheck,
  updateSupplierIndustry,
  addSourceCheck,
  type ReviewStepName,
  type AnalysisJob,
  type CreateSupplierInput,
  type LocalModelStatus,
  type RiskAssessment,
  type ResearchSource,
  type SourceCheck,
  type SourceCheckInput,
  type SourceMixItem,
  type SupplierFact,
  type OpenQuestionResolution,
  type SupplierAnalytics,
  type SupplierConnection,
  type SupplierDetail,
  type SupplierMatchCandidate,
  type SupplierReviewSummary,
  type SupplierSummary,
  type TimelineItem,
} from './api.ts'
import './styles.css'

const emptySupplierForm: CreateSupplierInput = {
  name: '',
  countryCode: '',
  industry: '',
  websiteUrl: '',
  runInitialAnalysis: true,
}

const supplierFolderOrderStorageKey = 'supplier-intelligence-folder-order'
const resolvedOpenQuestionsStoragePrefix = 'supplier-intelligence-resolved-open-questions'

type ReviewStep = ReviewStepName
type SourceFormState = SourceCheckInput

function App() {
  const [suppliers, setSuppliers] = useState<SupplierSummary[]>([])
  const [selectedSupplierId, setSelectedSupplierId] = useState<number | null>(null)
  const [supplier, setSupplier] = useState<SupplierDetail | null>(null)
  const [reviewSummary, setReviewSummary] = useState<SupplierReviewSummary | null>(null)
  const [supplierAnalytics, setSupplierAnalytics] = useState<SupplierAnalytics | null>(null)
  const [supplierConnections, setSupplierConnections] = useState<SupplierConnection[]>([])
  const [matchCandidates, setMatchCandidates] = useState<SupplierMatchCandidate[]>([])
  const [supplierForm, setSupplierForm] = useState<CreateSupplierInput>(emptySupplierForm)
  const [sourceForm, setSourceForm] = useState<SourceFormState>({
    sourceName: '',
    url: '',
    status: 'Reachable',
    notes: '',
  })
  const [websiteResearchUrl, setWebsiteResearchUrl] = useState('')
  const [editingSourceId, setEditingSourceId] = useState<number | null>(null)
  const [sourceMessage, setSourceMessage] = useState<string | null>(null)
  const [resolvedOpenQuestions, setResolvedOpenQuestions] = useState<string[]>([])
  const [openQuestionRecheckResult, setOpenQuestionRecheckResult] = useState<OpenQuestionResolution[] | null>(null)
  const [localModelStatus, setLocalModelStatus] = useState<LocalModelStatus | null>(null)
  const [activeAnalysisJob, setActiveAnalysisJob] = useState<AnalysisJob | null>(null)
  const [openRouterApiKey, setOpenRouterApiKey] = useState('')
  const [runtimeKeyMessage, setRuntimeKeyMessage] = useState<{ tone: 'ok' | 'warn' | 'error'; text: string } | null>(null)
  const [folderMessage, setFolderMessage] = useState<string | null>(null)
  const [dragOverIndustry, setDragOverIndustry] = useState<string | null>(null)
  const [rankTargetIndustry, setRankTargetIndustry] = useState<string | null>(null)
  const [folderOrder, setFolderOrder] = useState<string[]>(() => readStoredFolderOrder())
  const [loading, setLoading] = useState(true)
  const [checkingLocalModel, setCheckingLocalModel] = useState(false)
  const [savingOpenRouterKey, setSavingOpenRouterKey] = useState(false)
  const [creatingSupplier, setCreatingSupplier] = useState(false)
  const [archivingSupplier, setArchivingSupplier] = useState(false)
  const [queueingAnalysis, setQueueingAnalysis] = useState(false)
  const [savingSource, setSavingSource] = useState(false)
  const [researchingWebsite, setResearchingWebsite] = useState(false)
  const [recheckingOpenQuestions, setRecheckingOpenQuestions] = useState(false)
  const [deletingSourceId, setDeletingSourceId] = useState<number | null>(null)
  const [loadingMatchCandidates, setLoadingMatchCandidates] = useState(false)
  const [reviewingMatchCandidateId, setReviewingMatchCandidateId] = useState<number | null>(null)
  const [activeReviewStep, setActiveReviewStep] = useState<ReviewStep>('briefing')
  const [error, setError] = useState<string | null>(null)
  const visibleSuppliers = useMemo(() => buildVisibleSuppliers(suppliers), [suppliers])
  const supplierFolders = useMemo(
    () => groupSuppliersByIndustry(visibleSuppliers, folderOrder),
    [visibleSuppliers, folderOrder],
  )

  useEffect(() => {
    void loadSuppliers()
    void loadLocalModelStatus()
  }, [])

  useEffect(() => {
    if (selectedSupplierId === null) {
      setReviewSummary(null)
      setSupplierAnalytics(null)
      setSupplierConnections([])
      setActiveAnalysisJob(null)
      setMatchCandidates([])
      setResolvedOpenQuestions([])
      setOpenQuestionRecheckResult(null)
      return
    }

    setActiveReviewStep('briefing')
    setResolvedOpenQuestions(readResolvedOpenQuestions(selectedSupplierId))
    setOpenQuestionRecheckResult(null)
    void loadSupplier(selectedSupplierId)
    void loadReviewSummary(selectedSupplierId)
    void loadSupplierAnalytics(selectedSupplierId)
    void loadSupplierConnections(selectedSupplierId)
    void loadAnalysisJobs(selectedSupplierId)
    void loadMatchCandidates(selectedSupplierId)
  }, [selectedSupplierId])

  useEffect(() => {
    if (supplier === null || !activeAnalysisJob || !isActiveAnalysisJob(activeAnalysisJob)) {
      return
    }

    const intervalId = window.setInterval(() => {
      void refreshAnalysisRun(supplier.id, activeAnalysisJob.id)
    }, 3000)

    return () => window.clearInterval(intervalId)
  }, [supplier, activeAnalysisJob])

  async function loadSuppliers() {
    setLoading(true)
    setError(null)

    try {
      const result = await getSuppliers()
      const visibleResult = buildVisibleSuppliers(result)
      setSuppliers(result)
      setSelectedSupplierId((current) =>
        current && visibleResult.some((item) => item.id === current)
          ? current
          : visibleResult[0]?.id ?? null,
      )
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setLoading(false)
    }
  }

  async function loadSupplier(id: number) {
    setError(null)

    try {
      setSupplier(normalizeSupplierDetail(await getSupplier(id)))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadReviewSummary(id: number) {
    setError(null)

    try {
      setReviewSummary(await getSupplierReviewSummary(id))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadSupplierAnalytics(id: number) {
    setError(null)

    try {
      setSupplierAnalytics(await getSupplierAnalytics(id))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadSupplierConnections(id: number) {
    setError(null)

    try {
      setSupplierConnections(await getSupplierConnections(id))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function refreshSupplierResearchData(id: number) {
    await Promise.all([
      loadSupplier(id),
      loadReviewSummary(id),
      loadSupplierAnalytics(id),
      loadSupplierConnections(id),
    ])
  }

  async function loadAnalysisJobs(supplierId: number) {
    setError(null)

    try {
      const jobs = await getSupplierAnalysisJobs(supplierId)
      setActiveAnalysisJob(findActiveOrLatestAnalysisJob(jobs))
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function loadMatchCandidates(supplierId: number, quiet = false) {
    if (!quiet) {
      setLoadingMatchCandidates(true)
    }
    setError(null)

    try {
      setMatchCandidates(await getSupplierMatchCandidates(supplierId))
    } catch (exception) {
      setError(readError(exception))
    } finally {
      if (!quiet) {
        setLoadingMatchCandidates(false)
      }
    }
  }

  async function loadLocalModelStatus() {
    setCheckingLocalModel(true)

    try {
      const status = await getLocalModelStatus()
      setLocalModelStatus(status)
      return status
    } catch (exception) {
      const status = {
        provider: 'Unknown',
        baseUrl: 'Unknown',
        defaultModel: 'Unknown',
        isApiKeyConfigured: false,
        apiKeySource: 'Unknown',
        apiKeyFingerprint: '',
        apiKeyUpdatedAt: null,
        isReachable: false,
        errorMessage: readError(exception),
        models: [],
      }
      setLocalModelStatus(status)
      return status
    } finally {
      setCheckingLocalModel(false)
    }
  }

  async function saveOpenRouterKeyForRun(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setSavingOpenRouterKey(true)
    setError(null)

    try {
      await saveOpenRouterApiKey(openRouterApiKey)
      setOpenRouterApiKey('')
      const status = await loadLocalModelStatus()
      setRuntimeKeyMessage(buildRuntimeKeyMessage('save', status))
    } catch (exception) {
      setRuntimeKeyMessage({ tone: 'error', text: readError(exception) })
    } finally {
      setSavingOpenRouterKey(false)
    }
  }

  async function testOpenRouterConnection() {
    setSavingOpenRouterKey(true)
    setError(null)

    try {
      const status = await loadLocalModelStatus()
      setRuntimeKeyMessage(buildRuntimeKeyMessage('test', status))
    } catch (exception) {
      setRuntimeKeyMessage({ tone: 'error', text: readError(exception) })
    } finally {
      setSavingOpenRouterKey(false)
    }
  }

  async function clearOpenRouterKeyForRun() {
    setSavingOpenRouterKey(true)
    setError(null)

    try {
      await clearOpenRouterApiKey()
      const status = await loadLocalModelStatus()
      setRuntimeKeyMessage({
        tone: status.isApiKeyConfigured ? 'warn' : 'ok',
        text: status.isApiKeyConfigured
          ? `Runtime key cleared, but another runtime key is still active: ${status.apiKeyFingerprint}.`
          : 'Runtime key cleared. No OpenRouter key is configured now.',
      })
    } catch (exception) {
      setRuntimeKeyMessage({ tone: 'error', text: readError(exception) })
    } finally {
      setSavingOpenRouterKey(false)
    }
  }

  async function submitSource(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (supplier === null) {
      return
    }

    setSavingSource(true)
    setError(null)
    setSourceMessage(null)

    try {
      if (editingSourceId === null) {
        await addSourceCheck(supplier.id, sourceForm)
        setSourceMessage('Source added.')
      } else {
        await updateSourceCheck(supplier.id, editingSourceId, sourceForm)
        setSourceMessage('Source updated.')
      }

      setSourceForm({ sourceName: '', url: '', status: 'Reachable', notes: '' })
      setEditingSourceId(null)
      await refreshSupplierResearchData(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setSavingSource(false)
    }
  }

  async function researchWebsite(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (supplier === null) {
      return
    }

    setResearchingWebsite(true)
    setError(null)
    setSourceMessage(null)

    try {
      const createdSources = await researchWebsiteSource(supplier.id, { url: websiteResearchUrl })
      setWebsiteResearchUrl('')
      setSourceMessage(
        createdSources.length === 0
          ? 'Website already researched. No new source cards added.'
          : `Website researched. Added ${createdSources.length} source${createdSources.length === 1 ? '' : 's'}.`,
      )
      await refreshSupplierResearchData(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setResearchingWebsite(false)
    }
  }

  async function removeSource(sourceCheckId: number) {
    if (supplier === null) {
      return
    }

    setDeletingSourceId(sourceCheckId)
    setError(null)
    setSourceMessage(null)

    try {
      await deleteSourceCheck(supplier.id, sourceCheckId)
      setSourceMessage('Source removed.')
      await refreshSupplierResearchData(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setDeletingSourceId(null)
    }
  }

  async function recheckCurrentOpenQuestions() {
    if (supplier === null || openQuestionItems.length === 0) {
      return
    }

    setRecheckingOpenQuestions(true)
    setError(null)
    setOpenQuestionRecheckResult(null)

    try {
      const result = await recheckOpenQuestions(supplier.id, openQuestionItems)
      const nextResolvedQuestions = distinctText([
        ...resolvedOpenQuestions,
        ...result.resolved.map((item) => item.question),
      ])
      setResolvedOpenQuestions(nextResolvedQuestions)
      writeResolvedOpenQuestions(supplier.id, nextResolvedQuestions)
      setOpenQuestionRecheckResult([...result.resolved, ...result.unresolved])
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setRecheckingOpenQuestions(false)
    }
  }

  function startEditingSource(sourceCheck: SourceCheck) {
    setEditingSourceId(sourceCheck.id)
    setSourceForm({
      sourceName: sourceCheck.sourceName,
      url: sourceCheck.url,
      status: normalizeSourceStatus(sourceCheck.status),
      notes: sourceCheck.notes,
    })
    setSourceMessage('Editing source. Save changes or cancel.')
  }

  function cancelEditingSource() {
    setEditingSourceId(null)
    setSourceForm({ sourceName: '', url: '', status: 'Reachable', notes: '' })
    setSourceMessage(null)
  }

  async function submitSupplier(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault()
    setCreatingSupplier(true)
    setError(null)

    try {
      const createdSupplier = await createSupplier(supplierForm)
      const createdSupplierDetail = normalizeSupplierDetail(await getSupplier(createdSupplier.id))
      const supplierList = await getSuppliers()

      setSuppliers(supplierList)
      setSupplier(createdSupplierDetail)
      setSelectedSupplierId(createdSupplier.id)
      setMatchCandidates([])
      await loadReviewSummary(createdSupplier.id)
      await loadSupplierAnalytics(createdSupplier.id)
      await loadSupplierConnections(createdSupplier.id)
      await loadMatchCandidates(createdSupplier.id)
      setSupplierForm(emptySupplierForm)
      void loadLocalModelStatus()
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setCreatingSupplier(false)
    }
  }

  async function archiveSelectedSupplier() {
    if (supplier === null) {
      return
    }

    setArchivingSupplier(true)
    setError(null)

    try {
      await archiveSupplier(supplier.id)
      const supplierList = await getSuppliers()
      const visibleResult = buildVisibleSuppliers(supplierList)
      const nextSupplierId = visibleResult.find((item) => item.id !== supplier.id)?.id ?? null

      setSuppliers(supplierList)
      setSelectedSupplierId(nextSupplierId)
      setSupplier(null)
      setReviewSummary(null)
      setSupplierAnalytics(null)
      setSupplierConnections([])
      setMatchCandidates([])

      if (nextSupplierId !== null) {
        await loadSupplier(nextSupplierId)
        await loadReviewSummary(nextSupplierId)
        await loadSupplierAnalytics(nextSupplierId)
        await loadSupplierConnections(nextSupplierId)
        await loadMatchCandidates(nextSupplierId)
      }
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setArchivingSupplier(false)
    }
  }

  async function moveSuppliersToIndustry(supplierIds: number[], industry: string) {
    const ids = [...new Set(supplierIds)].filter((id) => {
      const item = visibleSuppliers.find((supplierItem) => supplierItem.id === id)
      return item && item.industry !== industry
    })

    if (ids.length === 0) {
      setFolderMessage('Already in this folder.')
      return
    }

    setError(null)
    setFolderMessage(`Moving ${ids.length} supplier${ids.length === 1 ? '' : 's'} to ${industry}...`)

    try {
      await Promise.all(ids.map((id) => updateSupplierIndustry(id, industry)))
      await loadSuppliers()

      if (selectedSupplierId !== null && ids.includes(selectedSupplierId)) {
        await loadSupplier(selectedSupplierId)
        await loadReviewSummary(selectedSupplierId)
        await loadSupplierAnalytics(selectedSupplierId)
        await loadSupplierConnections(selectedSupplierId)
      }

      setFolderMessage(`Moved ${ids.length} supplier${ids.length === 1 ? '' : 's'} to ${industry}.`)
    } catch (exception) {
      setError(readError(exception))
      setFolderMessage(null)
    } finally {
      setDragOverIndustry(null)
    }
  }

  function handleFolderDrop(event: React.DragEvent<HTMLElement>, targetIndustry: string) {
    event.preventDefault()
    const supplierId = Number(event.dataTransfer.getData('application/x-supplier-id'))
    const sourceIndustry = event.dataTransfer.getData('application/x-supplier-industry')
    const sourceFolder = event.dataTransfer.getData('application/x-folder-industry')

    if (Number.isFinite(supplierId) && supplierId > 0) {
      void moveSuppliersToIndustry([supplierId], targetIndustry)
      return
    }

    if (sourceFolder && sourceFolder !== targetIndustry) {
      const sourceSupplierIds = visibleSuppliers
        .filter((item) => item.industry === sourceFolder)
        .map((item) => item.id)
      void moveSuppliersToIndustry(sourceSupplierIds, targetIndustry)
      return
    }

    if (sourceIndustry && sourceIndustry === targetIndustry) {
      setFolderMessage('Already in this folder.')
    }
  }

  function moveFolderAbove(sourceIndustry: string, targetIndustry: string) {
    if (!sourceIndustry || sourceIndustry === targetIndustry) {
      setFolderMessage('Folder is already in that position.')
      return
    }

    const visibleIndustries = supplierFolders.map((folder) => folder.industry)
    const nextOrder = reorderIndustryFolders(visibleIndustries, folderOrder, sourceIndustry, targetIndustry)
    setFolderOrder(nextOrder)
    writeStoredFolderOrder(nextOrder)
    setFolderMessage(`Moved ${sourceIndustry} above ${targetIndustry}.`)
    setDragOverIndustry(null)
    setRankTargetIndustry(null)
  }

  function handleFolderRankDrop(event: React.DragEvent<HTMLElement>, targetIndustry: string) {
    event.preventDefault()
    event.stopPropagation()
    const sourceIndustry = event.dataTransfer.getData('application/x-folder-industry')
    moveFolderAbove(sourceIndustry, targetIndustry)
  }

  async function runAnalysis() {
    if (supplier === null) {
      return
    }

    setQueueingAnalysis(true)
    setError(null)

    try {
      const job = await queueSupplierAnalysis(supplier.id)
      setActiveAnalysisJob(job)
      await refreshAnalysisRun(supplier.id, job.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setQueueingAnalysis(false)
    }
  }

  async function refreshAnalysisRun(supplierId: number, jobId: number) {
    setError(null)

    try {
      const [job] = await Promise.all([
        getSupplierAnalysisJob(supplierId, jobId),
        loadSupplier(supplierId),
        loadReviewSummary(supplierId),
        loadSupplierAnalytics(supplierId),
        loadSupplierConnections(supplierId),
        loadMatchCandidates(supplierId, true),
      ])

      setActiveAnalysisJob(job)
      if (!isActiveAnalysisJob(job)) {
        void loadSuppliers()
      }
    } catch (exception) {
      setError(readError(exception))
    }
  }

  async function findMatchCandidates() {
    if (supplier === null) {
      return
    }

    setLoadingMatchCandidates(true)
    setError(null)

    try {
      setMatchCandidates(await suggestSupplierMatchCandidates(supplier.id))
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setLoadingMatchCandidates(false)
    }
  }

  async function confirmMatchCandidate(candidateId: number) {
    if (supplier === null) {
      return
    }

    setReviewingMatchCandidateId(candidateId)
    setError(null)

    try {
      await confirmSupplierMatchCandidate(supplier.id, candidateId)
      await loadMatchCandidates(supplier.id)
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setReviewingMatchCandidateId(null)
    }
  }

  async function rejectMatchCandidate(candidateId: number) {
    if (supplier === null) {
      return
    }

    setReviewingMatchCandidateId(candidateId)
    setError(null)

    try {
      await rejectSupplierMatchCandidate(supplier.id, candidateId)
      await loadMatchCandidates(supplier.id)
      await loadReviewSummary(supplier.id)
      await loadSupplierAnalytics(supplier.id)
    } catch (exception) {
      setError(readError(exception))
    } finally {
      setReviewingMatchCandidateId(null)
    }
  }

  const assessments = useMemo(
    () =>
      supplier
        ? [...safeArray(supplier.riskAssessments)].sort(
            (a: RiskAssessment, b: RiskAssessment) => Date.parse(b.createdAt) - Date.parse(a.createdAt),
          )
        : [],
    [supplier],
  )
  const latestAssessment = assessments[0]
  const latestEvidenceQuality = latestAssessment
    ? readEvidenceQuality(latestAssessment.evidenceSnapshotJson)
    : null
  const latestEvidenceSnapshot = latestAssessment
    ? readEvidenceSnapshot(latestAssessment.evidenceSnapshotJson)
    : null
  const reachableSourceCount = supplier
    ? supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
    : 0
  const nextEvidenceItems = supplier ? buildNextEvidenceItems(supplier, reachableSourceCount) : []
  const knownSupplierFacts = supplier ? buildKnownSupplierFacts(supplier.supplierFacts) : []
  const usefulResearchSources = supplier ? buildUsefulResearchSources(supplier.researchSources) : []
  const briefingFacts = supplier ? buildBriefingFacts(supplier, knownSupplierFacts, latestEvidenceSnapshot) : []
  const visibleMatchCandidates = buildVisibleMatchCandidates(matchCandidates)
  const confirmedIdentity = matchCandidates.find((candidate) => candidate.status === 'Confirmed') ?? null
  const hasConfirmedIdentity = confirmedIdentity !== null
  const knownInformation = supplier
    ? buildKnownInformation({
        supplier,
        confirmedIdentity,
        latestAssessment,
        latestEvidenceQuality,
        reachableSourceCount,
      })
    : []
  const nextAction = buildNextAction({
    hasConfirmedIdentity,
    loadingMatchCandidates,
    matchCandidateCount: visibleMatchCandidates.length,
    nextEvidenceCount: nextEvidenceItems.length,
    latestAssessment,
  })
  const displayKnownInformation = reviewSummary?.knownInformation ?? knownInformation
  const displayMissingInformation = reviewSummary?.missingInformation ?? nextEvidenceItems
  const displayNextAction = reviewSummary?.nextAction ?? nextAction
  const displayTrustSignals = reviewSummary?.trustSignals ?? null
  const openQuestionItems = buildOpenQuestionItems({
    displayMissingInformation,
    latestAssessment,
    latestEvidenceSnapshot,
    nextEvidenceItems,
  }).filter((item) => !resolvedOpenQuestions.some((resolved) => normalizeQuestionKey(resolved) === normalizeQuestionKey(item)))

  return (
    <main className="app-shell">
      <section className="sidebar">
        <div className="section-heading">
          <p>Supplier Intelligence</p>
          <span>{visibleSuppliers.length} suppliers</span>
        </div>

        <details className="supplier-create">
          <summary>New supplier</summary>

          <form onSubmit={submitSupplier}>
            <label>
              Name
              <input
                required
                value={supplierForm.name}
                onChange={(event) => setSupplierForm({ ...supplierForm, name: event.target.value })}
              />
            </label>

            <label>
              Country
              <input
                maxLength={2}
                required
                value={supplierForm.countryCode}
                onChange={(event) =>
                  setSupplierForm({ ...supplierForm, countryCode: event.target.value.toUpperCase() })
                }
              />
            </label>

            <label>
              Industry
              <input
                required
                value={supplierForm.industry}
                onChange={(event) => setSupplierForm({ ...supplierForm, industry: event.target.value })}
              />
            </label>

            <label>
              Website
              <input
                placeholder="https://supplier.example"
                type="url"
                value={supplierForm.websiteUrl ?? ''}
                onChange={(event) =>
                  setSupplierForm({
                    ...supplierForm,
                    websiteUrl: event.target.value === '' ? null : event.target.value,
                  })
                }
              />
            </label>

            <label className="checkbox-field">
              <input
                checked={supplierForm.runInitialAnalysis}
                type="checkbox"
                onChange={(event) =>
                  setSupplierForm({ ...supplierForm, runInitialAnalysis: event.target.checked })
                }
              />
              Run first analysis
            </label>

            <button className="primary" disabled={creatingSupplier} type="submit">
              {creatingSupplier ? 'Creating and analysing...' : 'Create and analyse'}
            </button>
          </form>
        </details>

        <div className="supplier-list" aria-label="Suppliers">
          {loading && <p className="muted">Loading suppliers...</p>}
          {!loading && visibleSuppliers.length === 0 && <p className="muted">No suppliers found.</p>}
          {folderMessage && <p className="folder-message">{folderMessage}</p>}
          {supplierFolders.map((folder) => (
            <details
              className={[
                'supplier-folder',
                dragOverIndustry === folder.industry ? 'drag-over' : '',
                rankTargetIndustry === folder.industry ? 'rank-over' : '',
              ].filter(Boolean).join(' ')}
              key={folder.industry}
              open
              onDragLeave={() => {
                setDragOverIndustry(null)
                setRankTargetIndustry(null)
              }}
              onDragOver={(event) => {
                event.preventDefault()
                setDragOverIndustry(folder.industry)
              }}
              onDrop={(event) => handleFolderDrop(event, folder.industry)}
            >
              <summary
                draggable
                onDragStart={(event) => {
                  event.dataTransfer.effectAllowed = 'move'
                  event.dataTransfer.setData('application/x-folder-industry', folder.industry)
                  event.dataTransfer.setData('text/plain', folder.industry)
                }}
                onDragOver={(event) => {
                  if (event.dataTransfer.types.includes('application/x-folder-industry')) {
                    event.preventDefault()
                    event.stopPropagation()
                    setRankTargetIndustry(folder.industry)
                    setDragOverIndustry(null)
                  }
                }}
                onDrop={(event) => handleFolderRankDrop(event, folder.industry)}
              >
                <span>{folder.industry}</span>
                <small>{folder.suppliers.length}</small>
              </summary>
              <div
                className="supplier-folder-list"
                onDragOver={(event) => {
                  event.preventDefault()
                  setDragOverIndustry(folder.industry)
                  setRankTargetIndustry(null)
                }}
              >
                {folder.suppliers.map((item) => (
                  <button
                    className={item.id === selectedSupplierId ? 'supplier-item active' : 'supplier-item'}
                    draggable
                    key={item.id}
                    onDragStart={(event) => {
                      event.dataTransfer.effectAllowed = 'move'
                      event.dataTransfer.setData('application/x-supplier-id', String(item.id))
                      event.dataTransfer.setData('application/x-supplier-industry', item.industry)
                      event.dataTransfer.setData('text/plain', item.name)
                    }}
                    onClick={() => setSelectedSupplierId(item.id)}
                    type="button"
                  >
                    <span>{item.name}</span>
                    <small>{item.countryCode}</small>
                    {item.websiteUrl && <small>{item.websiteUrl}</small>}
                  </button>
                ))}
              </div>
            </details>
          ))}
        </div>
      </section>

      <section className="detail">
        {error && <div className="error-banner">{error}</div>}

        {supplier ? (
          <>
            <div className="supplier-header">
              <div>
                <h1>{supplier.name}</h1>
                <p>
                  {supplier.countryCode} · {supplier.industry}
                </p>
                {supplier.websiteUrl && (
                  <a href={supplier.websiteUrl} rel="noreferrer" target="_blank">
                    {supplier.websiteUrl}
                  </a>
                )}
                <div className="supplier-facts">
                  <Fact label="Country" value={supplier.countryCode} />
                  <Fact label="Industry" value={supplier.industry} />
                </div>
              </div>
              <div className="supplier-header-actions">
                <button
                  className="primary"
                  disabled={queueingAnalysis || isActiveAnalysisJob(activeAnalysisJob)}
                  onClick={runAnalysis}
                  type="button"
                >
                  {queueingAnalysis
                    ? 'Queueing...'
                    : isActiveAnalysisJob(activeAnalysisJob)
                      ? 'Analysis running'
                      : 'Run analysis'}
                </button>
                <button
                  className="ghost"
                  disabled={archivingSupplier}
                  onClick={archiveSelectedSupplier}
                  type="button"
                >
                  {archivingSupplier ? 'Archiving...' : 'Archive'}
                </button>
              </div>
            </div>

            <section className="review-overview">
              <div className="next-action-card">
                <div className="section-heading">
                  <p>Research briefing</p>
                  <span>{reviewSummary?.headline ?? nextAction.status}</span>
                </div>
                <h2>{displayNextAction.title}</h2>
                <p>{displayNextAction.description}</p>
                <button
                  className="primary"
                  disabled={loadingMatchCandidates}
                  onClick={() => {
                    setActiveReviewStep(displayNextAction.step)
                  }}
                  type="button"
                >
                  {loadingMatchCandidates ? 'Loading...' : displayNextAction.buttonLabel}
                </button>
              </div>

              <section className="dashboard-card">
                <div className="section-heading">
                  <p>What the app found</p>
                  <span>{displayTrustSignals?.evidence ?? `${reachableSourceCount} sources`}</span>
                </div>
                <KnownInformationList items={displayKnownInformation} />
                {displayTrustSignals && (
                  <div className="trust-signal-row">
                    <StatusField label="Facts" value={displayTrustSignals.identity} />
                    <StatusField label="Sources" value={displayTrustSignals.evidence} />
                    <StatusField label="Source issues" value={displayTrustSignals.certifications} />
                    <StatusField label="Company source" value={displayTrustSignals.risk} />
                  </div>
                )}
              </section>
            </section>

            <nav className="review-stepper" aria-label="Supplier review steps">
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Briefing"
                step="briefing"
                summary={briefingFacts.length > 0 ? `${briefingFacts.length} facts` : 'Overview'}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Sources"
                step="sources"
                summary={`${supplier.sourceChecks.length} sources`}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Open questions"
                step="questions"
                summary={`${openQuestionItems.length} open`}
                onSelect={setActiveReviewStep}
              />
              <ReviewStepButton
                activeStep={activeReviewStep}
                label="Connections"
                step="connections"
                summary={`${supplierConnections.length} found`}
                onSelect={setActiveReviewStep}
              />
            </nav>

            <section className="review-workspace">
              {activeReviewStep === 'briefing' && (
                <SupplierBriefingMemo
                  displayKnownInformation={displayKnownInformation}
                  latestEvidenceSnapshot={latestEvidenceSnapshot}
                  openQuestionItems={openQuestionItems}
                  supplier={supplier}
                />
              )}

              {activeReviewStep === 'sources' && (
                <>
                  <details className="source-add-panel">
                    <summary>
                      <span>Add source</span>
                      <small>{sourceMessage ?? 'Manual or website research'}</small>
                    </summary>
                    <div className="source-add-grid">
                      <form className="source-check-form" onSubmit={submitSource}>
                        <label>
                          Source name
                          <input
                            required
                            value={sourceForm.sourceName}
                            onChange={(event) => setSourceForm({ ...sourceForm, sourceName: event.target.value })}
                          />
                        </label>
                        <label>
                          URL
                          <input
                            required
                            type="url"
                            value={sourceForm.url}
                            onChange={(event) => setSourceForm({ ...sourceForm, url: event.target.value })}
                          />
                        </label>
                        <label>
                          Status
                          <select
                            value={sourceForm.status}
                            onChange={(event) =>
                              setSourceForm({ ...sourceForm, status: normalizeSourceStatus(event.target.value) })
                            }
                          >
                            <option value="Reachable">Reachable</option>
                            <option value="NotChecked">Not checked</option>
                            <option value="Blocked">Blocked</option>
                            <option value="Failed">Failed</option>
                          </select>
                        </label>
                        <label>
                          What is inside this source
                          <textarea
                            required={sourceForm.status === 'Reachable' || sourceForm.status === 'Blocked' || sourceForm.status === 'Failed'}
                            value={sourceForm.notes}
                            onChange={(event) => setSourceForm({ ...sourceForm, notes: event.target.value })}
                          />
                        </label>
                        <div className="source-actions">
                          <button className="primary" disabled={savingSource} type="submit">
                            {savingSource ? 'Saving...' : editingSourceId === null ? 'Add source' : 'Save source'}
                          </button>
                          {editingSourceId !== null && (
                            <button className="ghost" disabled={savingSource} type="button" onClick={cancelEditingSource}>
                              Cancel
                            </button>
                          )}
                        </div>
                      </form>

                      <form className="source-check-form" onSubmit={researchWebsite}>
                        <label>
                          Website link for AI research
                          <input
                            required
                            placeholder="https://supplier.example"
                            type="url"
                            value={websiteResearchUrl}
                            onChange={(event) => setWebsiteResearchUrl(event.target.value)}
                          />
                        </label>
                        <p className="muted evidence-empty">
                          Paste a website. The app fetches useful pages, stores them in Sources found, and refreshes extracted facts.
                        </p>
                        <button className="primary" disabled={researchingWebsite} type="submit">
                          {researchingWebsite ? 'Researching...' : 'Research website'}
                        </button>
                      </form>
                    </div>
                  </details>

                  <section className="evidence-panel">
                    <div className="workflow-card">
                      <div className="section-heading">
                        <p>Sources found</p>
                        <span>{supplier.sourceChecks.length} sources</span>
                      </div>
                      <CompactSourceCheckList
                        deletingSourceId={deletingSourceId}
                        sourceChecks={supplier.sourceChecks}
                        onDelete={removeSource}
                        onEdit={startEditingSource}
                      />
                    </div>
                  </section>

                  {(knownSupplierFacts.length > 0 || usefulResearchSources.length > 0) && (
                    <details className="secondary-details">
                      <summary>Validated facts and research notes</summary>
                      <section className="supplier-intelligence-panel">
                        <SnapshotList title="Supplier facts" items={knownSupplierFacts} />
                        <SnapshotList title="Research sources" items={usefulResearchSources} />
                      </section>
                    </details>
                  )}
                </>
              )}

              {activeReviewStep === 'questions' && (
                <>
                  <section className="risk-memo-panel">
                    <div className="section-heading">
                      <p>Open questions</p>
                      <span>{openQuestionItems.length} unresolved</span>
                    </div>
                    <div className="open-question-toolbar">
                      <p>Re-check uses stored facts and sources first. It does not repeat website research.</p>
                      <button
                        className="primary"
                        disabled={recheckingOpenQuestions || openQuestionItems.length === 0}
                        type="button"
                        onClick={() => void recheckCurrentOpenQuestions()}
                      >
                        {recheckingOpenQuestions ? 'Re-checking...' : 'Re-check with AI'}
                      </button>
                    </div>
                    {openQuestionItems.length === 0 ? (
                      <p className="muted evidence-empty">No open questions found from the current research.</p>
                    ) : (
                      <ul>
                        {openQuestionItems.map((item) => (
                          <li key={item}>{item}</li>
                        ))}
                      </ul>
                    )}
                    {openQuestionRecheckResult && (
                      <div className="open-question-results">
                        {openQuestionRecheckResult.map((item) => (
                          <article className={`open-question-result open-question-${item.status}`} key={`${item.status}-${item.question}`}>
                            <strong>{item.status === 'resolved' ? 'Resolved' : 'Still open'}</strong>
                            <span>{item.question}</span>
                            <p>{item.evidenceNote}</p>
                            {item.sourceName && <small>{item.sourceName}</small>}
                          </article>
                        ))}
                      </div>
                    )}
                  </section>

                  <details className="secondary-details">
                    <summary>Evidence behind this risk memo</summary>
                    {latestEvidenceQuality && (
                      <section className="quality-panel">
                        <div className="section-heading">
                          <p>Source status</p>
                          <span>{latestEvidenceQuality.hasBlockedOrFailedSource ? 'Some source issues' : 'No blocked source flags'}</span>
                        </div>
                        <div className="quality-grid">
                          <StatusField label="Website source" value={formatBoolean(latestEvidenceQuality.hasReachableWebsite)} />
                          <StatusField label="Company source" value={formatBoolean(latestEvidenceQuality.hasReachableRegistrySource)} />
                          <StatusField label="Tax/company clue" value={formatBoolean(latestEvidenceQuality.hasReachableVatSource)} />
                          <StatusField label="Blocked or failed source" value={formatBoolean(latestEvidenceQuality.hasBlockedOrFailedSource)} />
                        </div>
                      </section>
                    )}

                    {latestEvidenceSnapshot && (
                      <section className="supplier-intelligence-panel">
                        <div className="section-heading">
                          <p>Supplier snapshot</p>
                          <span>{latestEvidenceSnapshot.supplierProfile.isSparseInput ? 'Limited public profile' : 'Detailed public profile'}</span>
                        </div>
                        <div className="supplier-intelligence-grid">
                          <StatusField label="Profile depth" value={latestEvidenceSnapshot.supplierProfile.isSparseInput ? 'Limited' : 'Detailed'} />
                          <StatusField label="Research note" value={latestEvidenceSnapshot.riskDecision.reason} />
                        </div>
                        <p className="company-description">{latestEvidenceSnapshot.companySummary.description}</p>
                        <SnapshotList title="External evidence" items={latestEvidenceSnapshot.companySummary.externalHighlights} />
                        <SnapshotList title="Own website evidence" items={latestEvidenceSnapshot.companySummary.ownWebsiteHighlights} />
                        <SnapshotList title="Known facts" items={latestEvidenceSnapshot.automationFindings} />
                      </section>
                    )}
                  </details>

                  <details className="secondary-details">
                    <summary>Full research memo text</summary>
                    <section className="workspace read-only-workspace">
                      <div className="assessment-list">
                        {assessments.length === 0 ? (
                          <p className="muted evidence-empty">No assessments yet.</p>
                        ) : (
                          assessments.map((assessment) => (
                            <article className="assessment" key={assessment.id}>
                              <div className="assessment-topline">
                                <strong>Research memo</strong>
                                <span>{formatShortDate(assessment.createdAt)}</span>
                              </div>
                              <p className="assessment-summary">{assessment.summaryMarkdown}</p>
                              <details className="assessment-full-text">
                                <summary>Detailed reasoning</summary>
                                <pre>{assessment.summaryMarkdown}</pre>
                              </details>
                            </article>
                          ))
                        )}
                      </div>
                    </section>
                  </details>
                </>
              )}

              {activeReviewStep === 'connections' && (
                <section className="report-panel">
                  <div className="section-heading">
                    <p>Connections</p>
                    <span>{supplierConnections.length} related suppliers</span>
                  </div>
                  <SupplierConnectionPanel connections={supplierConnections} supplier={supplier} />

                  <details className="secondary-details">
                    <summary>Technical details</summary>
                    <section className="analysis-job-panel">
                      <div className="section-heading">
                        <p>Analysis jobs</p>
                        <span>{supplier.analysisJobs.length} total</span>
                      </div>
                      <AnalysisJobList jobs={supplier.analysisJobs} />
                    </section>

                    <section className="local-model-panel">
                      <div className="section-heading">
                        <p>Model runtime</p>
                        <span>{checkingLocalModel ? 'checking' : localModelStatus?.isReachable ? 'online' : 'offline'}</span>
                      </div>
                      <div className="local-model-grid">
                        <StatusField label="Provider" value={localModelStatus?.provider ?? 'Unknown'} />
                        <StatusField label="Default model" value={localModelStatus?.defaultModel ?? 'Unknown'} />
                        <StatusField label="Base URL" value={localModelStatus?.baseUrl ?? 'Unknown'} />
                        <StatusField
                          label="API key"
                          value={
                            localModelStatus?.isApiKeyConfigured
                              ? `Configured (${localModelStatus.apiKeySource}${localModelStatus.apiKeyFingerprint ? ` ${localModelStatus.apiKeyFingerprint}` : ''})`
                              : 'Missing'
                          }
                        />
                      </div>
                      <form className="runtime-key-form" onSubmit={saveOpenRouterKeyForRun}>
                        <label>
                          OpenRouter API key
                          <input
                            autoComplete="off"
                            placeholder="sk-or-..."
                            type="password"
                            value={openRouterApiKey}
                            onChange={(event) => setOpenRouterApiKey(event.target.value)}
                          />
                        </label>
                        <div className="runtime-key-actions">
                          <button
                            className="primary"
                            disabled={savingOpenRouterKey || openRouterApiKey.trim().length === 0}
                            type="submit"
                          >
                            {savingOpenRouterKey ? 'Saving...' : 'Save key for this run'}
                          </button>
                          <button
                            className="ghost"
                            disabled={savingOpenRouterKey}
                            type="button"
                            onClick={() => void testOpenRouterConnection()}
                          >
                            {savingOpenRouterKey ? 'Testing...' : 'Test connection'}
                          </button>
                          <button
                            className="ghost"
                            disabled={savingOpenRouterKey || !localModelStatus?.isApiKeyConfigured}
                            type="button"
                            onClick={() => void clearOpenRouterKeyForRun()}
                          >
                            Clear runtime key
                          </button>
                        </div>
                      </form>
                      {runtimeKeyMessage && (
                        <p className={`runtime-key-message runtime-key-${runtimeKeyMessage.tone}`}>
                          {runtimeKeyMessage.text}
                        </p>
                      )}
                      <p className="runtime-key-note">
                        Saving replaces the runtime key. Clearing it removes the key for this backend run.
                      </p>
                      {localModelStatus?.errorMessage && <p className="local-model-error">{localModelStatus.errorMessage}</p>}
                      {localModelStatus?.models.length ? (
                        <div className="model-list">
                          {localModelStatus.models.map((model) => (
                            <span key={model.name}>{model.name}</span>
                          ))}
                        </div>
                      ) : (
                        <p className="muted local-model-empty">No provider models reported.</p>
                      )}
                    </section>
                  </details>
                </section>
              )}
            </section>

            {activeAnalysisJob && (
              <AnalysisRunPanel
                analytics={supplierAnalytics}
                job={activeAnalysisJob}
                matchCandidates={matchCandidates}
                supplier={supplier}
                latestAssessment={latestAssessment}
              />
            )}
          </>
        ) : (
          <p className="muted">Select a supplier.</p>
        )}
      </section>
    </main>
  )
}

function ReviewStepButton({
  activeStep,
  label,
  step,
  summary,
  onSelect,
}: {
  activeStep: ReviewStep
  label: string
  step: ReviewStep
  summary: string
  onSelect: (step: ReviewStep) => void
}) {
  return (
    <button
      className={activeStep === step ? 'review-step active' : 'review-step'}
      onClick={() => onSelect(step)}
      type="button"
    >
      <span>{label}</span>
      <strong>{summary}</strong>
    </button>
  )
}

function StatusField({ label, value }: { label: string; value: string }) {
  return (
    <div className="status-field">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function KnownInformationList({ items }: { items: string[] }) {
  return (
    <ul className="known-info-list">
      {items.map((item) => (
        <li key={item}>{item}</li>
      ))}
    </ul>
  )
}

function SupplierBriefingMemo({
  displayKnownInformation,
  latestEvidenceSnapshot,
  openQuestionItems,
  supplier,
}: {
  displayKnownInformation: string[]
  latestEvidenceSnapshot: EvidenceSnapshot | null
  openQuestionItems: string[]
  supplier: SupplierDetail
}) {
  const companySummary = buildBriefCompanySummary(supplier, latestEvidenceSnapshot)
    ?? 'No supplier summary has been extracted yet. Add sources or research the website to build the briefing.'
  const productsAndServices = buildFactSectionItems(supplier.supplierFacts, ['ProductsAndServices'])
  const locationsAndMarkets = distinctText([
    ...buildFactSectionItems(supplier.supplierFacts, ['LocationsAndMarkets', 'LegalIdentity', 'RegistryEvidence']),
    ...buildLocationCluesFromSources(supplier.sourceChecks),
  ]).slice(0, 5)
  const sourceFindings = distinctText([
    ...(latestEvidenceSnapshot?.companySummary.ownWebsiteHighlights ?? []),
    ...(latestEvidenceSnapshot?.companySummary.externalHighlights ?? []),
    ...buildFactSectionItems(supplier.supplierFacts, [
      'WebsiteEvidence',
      'RegistryEvidence',
      'VatEvidence',
      'QualitySystem',
      'SustainabilityAndCompliance',
      'CertificationClaim',
    ]),
  ].map((item) => formatBriefingBullet(item))).slice(0, 6)
  const sourcesUsed = buildBriefingSourcesUsed(supplier.sourceChecks)

  return (
    <section className="briefing-paper">
      <header className="briefing-paper-header">
        <div>
          <span>Supplier research memo</span>
          <h2>{supplier.name}</h2>
          <p>
            {supplier.countryCode} · {supplier.industry}
          </p>
        </div>
        <dl>
          <div>
            <dt>Website</dt>
            <dd>{supplier.websiteUrl ? readHostname(supplier.websiteUrl) : 'Not provided'}</dd>
          </div>
          <div>
            <dt>Sources</dt>
            <dd>{supplier.sourceChecks.length}</dd>
          </div>
          <div>
            <dt>Facts</dt>
            <dd>{supplier.supplierFacts.length}</dd>
          </div>
        </dl>
      </header>

      <section className="briefing-paper-section briefing-summary">
        <h3>What this supplier appears to do</h3>
        <p>{companySummary}</p>
      </section>

      <div className="briefing-paper-grid">
        <BriefingMemoList
          emptyText="No product or service details extracted yet."
          items={productsAndServices.length > 0 ? productsAndServices : displayKnownInformation.slice(0, 3)}
          title="Products / services found"
        />
        <BriefingMemoList
          emptyText="No location or market footprint details extracted yet."
          items={locationsAndMarkets}
          title="Locations / market footprint"
        />
      </div>

      <BriefingMemoList
        emptyText="No concrete source findings yet."
        items={sourceFindings}
        title="Important source findings"
      />

      <BriefingMemoList
        emptyText="No sources used yet."
        items={sourcesUsed}
        title="Sources used"
      />
    </section>
  )
}

function BriefingMemoList({
  emptyText,
  items,
  title,
}: {
  emptyText: string
  items: string[]
  title: string
}) {
  return (
    <section className="briefing-paper-section">
      <h3>{title}</h3>
      {items.length === 0 ? (
        <p className="briefing-empty">{emptyText}</p>
      ) : (
        <ul>
          {items.map((item, index) => (
            <li key={`${title}-${item}-${index}`}>{item}</li>
          ))}
        </ul>
      )}
    </section>
  )
}

function SupplierAnalyticsPanel({ analytics }: { analytics: SupplierAnalytics }) {
  return (
    <section className="analytics-panel">
      <div className="section-heading">
        <p>Research activity</p>
        <span>{analytics.timeline.length} events</span>
      </div>
      <div className="analytics-grid">
        <SourceMixList items={analytics.sourceMix} />
      </div>
      <div className="analytics-grid">
        <SnapshotList title="Useful findings" items={analytics.strongestSignals} />
        <SnapshotList title="Still unclear" items={analytics.weakestGaps} />
      </div>
      <TimelineList items={analytics.timeline} />
    </section>
  )
}

function SupplierConnectionPanel({
  connections,
  supplier,
}: {
  connections: SupplierConnection[]
  supplier: SupplierDetail
}) {
  if (connections.length === 0) {
    return (
      <p className="muted evidence-empty">
        No related suppliers found yet. Add more analysed suppliers to build a useful comparison map.
      </p>
    )
  }

  return (
    <div className="connection-workspace">
      <SupplierConnectionGraph connections={connections} supplier={supplier} />
      <div className="connection-list">
        {connections.map((connection) => (
          <article className="connection-card" id={`connection-${connection.supplierId}`} key={connection.supplierId}>
            <div className="connection-card-title">
              <div>
                <strong>{connection.supplierName}</strong>
                <span>
                  {connection.countryCode} · {connection.industry}
                </span>
              </div>
              <small>{connection.strengthLabel}</small>
            </div>
            <ul>
              {connection.reasons.slice(0, 3).map((reason) => (
                <li key={reason}>{reason}</li>
              ))}
            </ul>
            {connection.sharedTerms.length > 0 && (
              <div className="connection-terms">
                {connection.sharedTerms.map((term) => (
                  <span key={term}>{term}</span>
                ))}
              </div>
            )}
            {connection.websiteUrl && (
              <a className="source-open-link" href={connection.websiteUrl} rel="noreferrer" target="_blank">
                Open supplier website
              </a>
            )}
          </article>
        ))}
      </div>
    </div>
  )
}

function SupplierConnectionGraph({
  connections,
  supplier,
}: {
  connections: SupplierConnection[]
  supplier: SupplierDetail
}) {
  const visibleConnections = connections.slice(0, 5)
  const center = { x: 110, y: 92 }
  const nodeStartX = 270
  const nodeGap = 110
  const nodes = visibleConnections.map((connection, index) => {
    const rowOffset = index % 2 === 0 ? -28 : 28

    return {
      connection,
      x: nodeStartX + index * nodeGap,
      y: center.y + rowOffset,
    }
  })

  return (
    <section className="connection-graph-panel">
      <div className="section-heading">
        <p>Relationship map</p>
        <span>{visibleConnections.length} visible</span>
      </div>
      <svg className="connection-graph" role="img" viewBox="0 0 760 184">
        <title>Supplier relationship map for {supplier.name}</title>
        {nodes.map((node) => (
          <g key={`edge-${node.connection.supplierId}`}>
            <line
              className={`connection-edge connection-edge-${connectionStrengthClass(node.connection.strengthLabel)}`}
              x1={center.x}
              x2={node.x}
              y1={center.y}
              y2={node.y}
            />
          </g>
        ))}

        <g className="connection-node connection-node-main">
          <rect height="52" rx="0" width="150" x={center.x - 75} y={center.y - 26} />
          <text x={center.x} y={center.y - 3}>{shortenText(supplier.name, 18)}</text>
          <text className="connection-node-subtitle" x={center.x} y={center.y + 15}>selected supplier</text>
        </g>

        {nodes.map((node) => (
          <a href={`#connection-${node.connection.supplierId}`} key={`node-${node.connection.supplierId}`}>
            <g className={`connection-node connection-node-${connectionStrengthClass(node.connection.strengthLabel)}`}>
              <rect height="48" rx="0" width="96" x={node.x - 48} y={node.y - 24} />
              <text x={node.x} y={node.y - 4}>{shortenText(node.connection.supplierName, 13)}</text>
              <text className="connection-node-subtitle" x={node.x} y={node.y + 13}>
                {node.connection.strengthLabel.replace(' similarity', '')}
              </text>
            </g>
          </a>
        ))}
      </svg>
    </section>
  )
}

function AnalysisRunPanel({
  analytics,
  job,
  matchCandidates,
  supplier,
  latestAssessment,
}: {
  analytics: SupplierAnalytics | null
  job: AnalysisJob
  matchCandidates: SupplierMatchCandidate[]
  supplier: SupplierDetail
  latestAssessment: RiskAssessment | undefined
}) {
  const stages = buildAnalysisStages({ analytics, job, matchCandidates, supplier, latestAssessment })
  const reachableSourceCount = supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
  const failedSourceCount = supplier.sourceChecks.filter(
    (sourceCheck) => sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed',
  ).length

  return (
    <section className={`analysis-run-panel analysis-run-${job.status.toLowerCase()}`}>
      <div className="section-heading">
        <p>{isActiveAnalysisJob(job) ? 'Analysis running' : `Analysis ${job.status.toLowerCase()}`}</p>
        <span>{job.progressMessage}</span>
      </div>
      <div className="analysis-run-grid">
        <div className="analysis-stage-list">
          {stages.map((stage) => (
            <div className={`analysis-stage analysis-stage-${stage.status}`} key={stage.label}>
              <span>{formatStageMarker(stage.status)}</span>
              <div>
                <strong>{stage.label}</strong>
                <small>{stage.detail}</small>
              </div>
            </div>
          ))}
        </div>
        <div className="analysis-live-summary">
          <StatusField label="Sources found" value={`${reachableSourceCount} reachable / ${failedSourceCount} failed`} />
          <StatusField label="Facts extracted" value={`${supplier.supplierFacts.length}`} />
          <StatusField label="Research memo" value={latestAssessment ? 'Available' : 'Pending'} />
          <StatusField label="Connections" value={analytics ? 'Ready after refresh' : 'Pending'} />
        </div>
      </div>
      {job.errorMessage && <p className="analysis-run-error">{job.errorMessage}</p>}
    </section>
  )
}

function SourceMixList({ items }: { items: SourceMixItem[] }) {
  const maxCount = Math.max(1, ...items.map((item) => item.count))

  return (
    <div className="source-mix-panel">
      <div className="trust-score-header">
        <strong>Source mix</strong>
        <span>{items.reduce((total, item) => total + item.count, 0)} records</span>
      </div>
      <div className="source-mix-list">
        {items.map((item) => (
          <div className="source-mix-row" key={item.label}>
            <div>
              <strong>{item.label}</strong>
              <span>{item.status}</span>
            </div>
            <div className="trust-bar-track">
              <span style={{ width: `${(item.count / maxCount) * 100}%` }} />
            </div>
            <strong>{item.count}</strong>
          </div>
        ))}
      </div>
    </div>
  )
}

function TimelineList({ items }: { items: TimelineItem[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="timeline-panel">
      <strong>Review timeline</strong>
      <div className="timeline-list">
        {items.map((item) => (
          <article className="timeline-item" key={`${item.occurredAt}-${item.type}-${item.title}`}>
            <span>{formatShortDate(item.occurredAt)}</span>
            <div>
              <strong>{item.title}</strong>
              <p>{item.description}</p>
            </div>
            <small>{item.status}</small>
          </article>
        ))}
      </div>
    </div>
  )
}

function Fact({ label, value }: { label: string; value: string | null }) {
  if (!value) {
    return null
  }

  return (
    <span>
      <strong>{label}</strong>
      {value}
    </span>
  )
}

function MatchCandidateList({
  candidates,
  isLoading,
  reviewingCandidateId,
  onConfirm,
  onReject,
}: {
  candidates: SupplierMatchCandidate[]
  isLoading: boolean
  reviewingCandidateId: number | null
  onConfirm: (candidateId: number) => void
  onReject: (candidateId: number) => void
}) {
  if (isLoading && candidates.length === 0) {
    return <p className="muted evidence-empty">Finding possible supplier identities...</p>
  }

  if (candidates.length === 0) {
    return <p className="muted evidence-empty">No match candidates proposed yet.</p>
  }

  return (
    <div className="match-candidate-list">
      {[...candidates]
        .sort(compareMatchCandidates)
        .map((candidate) => {
          const isReviewing = reviewingCandidateId === candidate.id
          const isTerminal = candidate.status === 'Confirmed' || candidate.status === 'Rejected'

          return (
            <article className={`match-candidate match-${candidate.status.toLowerCase()}`} key={candidate.id}>
              <div className="match-candidate-title">
                <div>
                  <strong>{formatCandidateDisplayName(candidate.candidateName)}</strong>
                  <span>{formatCandidateLocation(candidate)}</span>
                </div>
                <span className="match-confidence">
                  {formatMatchConfidence(candidate.confidenceScore)} · {candidate.confidenceScore}/100
                </span>
              </div>
              <p>{candidate.matchReason}</p>
              <div className="match-candidate-meta">
                <span>{formatCandidateStatus(candidate.status)}</span>
                {candidate.sourceName && <span>{candidate.sourceName}</span>}
                {candidate.websiteUrl && (
                  <a href={candidate.websiteUrl} rel="noreferrer" target="_blank">
                    Website
                  </a>
                )}
                {candidate.sourceUrl && candidate.sourceUrl !== candidate.websiteUrl && (
                  <a href={candidate.sourceUrl} rel="noreferrer" target="_blank">
                    Source
                  </a>
                )}
              </div>
              <div className="match-candidate-actions">
                <button
                  className="primary"
                  disabled={isReviewing || candidate.status === 'Confirmed'}
                  onClick={() => onConfirm(candidate.id)}
                  type="button"
                >
                  {candidate.status === 'Confirmed' ? 'Saved match' : isReviewing ? 'Saving...' : 'Save match'}
                </button>
                <button
                  className="ghost"
                  disabled={isReviewing || isTerminal}
                  onClick={() => onReject(candidate.id)}
                  type="button"
                >
                  Dismiss
                </button>
              </div>
            </article>
          )
        })}
    </div>
  )
}

function isActiveAnalysisJob(job: AnalysisJob | null | undefined) {
  return job?.status === 'Queued' || job?.status === 'Running'
}

function findActiveOrLatestAnalysisJob(jobs: AnalysisJob[]) {
  const sortedJobs = [...safeArray(jobs)].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))

  return sortedJobs.find(isActiveAnalysisJob) ?? sortedJobs[0] ?? null
}

type AnalysisStageStatus = 'done' | 'active' | 'pending' | 'failed'

type AnalysisStage = {
  label: string
  detail: string
  status: AnalysisStageStatus
}

function buildAnalysisStages({
  analytics,
  job,
  matchCandidates,
  supplier,
  latestAssessment,
}: {
  analytics: SupplierAnalytics | null
  job: AnalysisJob
  matchCandidates: SupplierMatchCandidate[]
  supplier: SupplierDetail
  latestAssessment: RiskAssessment | undefined
}): AnalysisStage[] {
  const progress = job.progressMessage.toLowerCase()
  const isFailed = job.status === 'Failed'
  const isCompleted = job.status === 'Completed'
  const reachableSourceCount = supplier.sourceChecks.filter((sourceCheck) => sourceCheck.status === 'Reachable').length
  const websiteChecked = supplier.sourceChecks.some((sourceCheck) =>
    sourceCheck.sourceName.toLowerCase().includes('website'),
  )
  const factsExtracted = supplier.supplierFacts.length > 0
  const researchMemoReady = Boolean(latestAssessment)
  const analyticsReady = Boolean(analytics)

  return [
    {
      label: 'Queued',
      detail: formatAnalysisJobTime(job.createdAt),
      status: stageStatus({ done: job.status !== 'Queued', active: job.status === 'Queued', isFailed }),
    },
    {
      label: 'Website checked',
      detail: websiteChecked ? 'Website evidence stored' : 'Skipped if no website exists',
      status: stageStatus({
        done: websiteChecked || isCompleted,
        active: progress.includes('website'),
        isFailed,
      }),
    },
    {
      label: 'Public sources',
      detail: reachableSourceCount > 0 ? `${reachableSourceCount} reachable source${reachableSourceCount === 1 ? '' : 's'}` : 'Searching sources',
      status: stageStatus({
        done: reachableSourceCount > 0 || isCompleted,
        active: progress.includes('search') || progress.includes('evidence'),
        isFailed,
      }),
    },
    {
      label: 'Facts extracted',
      detail: factsExtracted ? `${supplier.supplierFacts.length} fact${supplier.supplierFacts.length === 1 ? '' : 's'} stored` : 'Waiting for source text',
      status: stageStatus({
        done: factsExtracted || isCompleted,
        active: progress.includes('facts'),
        isFailed,
      }),
    },
    {
      label: 'Research memo',
      detail: latestAssessment ? 'Open questions summarized' : 'Not generated yet',
      status: stageStatus({
        done: researchMemoReady || isCompleted,
        active: progress.includes('risk memo'),
        isFailed,
      }),
    },
    {
      label: 'Briefing refreshed',
      detail: analytics ? 'Sources and timeline updated' : 'Waiting for analysis data',
      status: stageStatus({
        done: analyticsReady && isCompleted,
        active: progress.includes('analytics'),
        isFailed,
      }),
    },
  ]
}

function stageStatus({
  done,
  active,
  isFailed,
}: {
  done: boolean
  active: boolean
  isFailed: boolean
}): AnalysisStageStatus {
  if (done) {
    return 'done'
  }

  if (isFailed) {
    return 'failed'
  }

  return active ? 'active' : 'pending'
}

function formatStageMarker(status: AnalysisStageStatus) {
  if (status === 'done') {
    return 'OK'
  }

  if (status === 'active') {
    return '...'
  }

  if (status === 'failed') {
    return 'ERR'
  }

  return '--'
}

function formatAnalysisJobTime(value: string) {
  return `Started ${formatShortDate(value)}`
}

function safeArray<T>(value: T[] | null | undefined) {
  return Array.isArray(value) ? value : []
}

function normalizeSupplierDetail(detail: SupplierDetail): SupplierDetail {
  return {
    ...detail,
    isArchived: detail.isArchived ?? false,
    analysisJobs: safeArray(detail.analysisJobs),
    certifications: safeArray(detail.certifications),
    sourceChecks: safeArray(detail.sourceChecks),
    researchSources: safeArray(detail.researchSources),
    supplierFacts: safeArray(detail.supplierFacts),
    riskAssessments: safeArray(detail.riskAssessments),
  }
}

function buildVisibleSuppliers(items: SupplierSummary[]) {
  const visible = new Map<string, SupplierSummary>()

  for (const item of items) {
    if (isDevelopmentSupplier(item)) {
      continue
    }

    const key = buildSupplierIdentityKey(item)
    if (!visible.has(key)) {
      visible.set(key, item)
    }
  }

  return [...visible.values()]
}

function groupSuppliersByIndustry(items: SupplierSummary[], folderOrder: string[]) {
  const groups = new Map<string, SupplierSummary[]>()

  for (const item of items) {
    const key = item.industry.trim() || 'Unsorted'
    groups.set(key, [...(groups.get(key) ?? []), item])
  }

  const orderIndex = new Map(folderOrder.map((industry, index) => [industry, index]))

  return [...groups.entries()]
    .map(([industry, suppliers]) => ({
      industry,
      suppliers: [...suppliers].sort((left, right) => left.name.localeCompare(right.name)),
    }))
    .sort((left, right) => {
      const leftIndex = orderIndex.get(left.industry)
      const rightIndex = orderIndex.get(right.industry)

      if (leftIndex !== undefined && rightIndex !== undefined) {
        return leftIndex - rightIndex
      }

      if (leftIndex !== undefined) {
        return -1
      }

      if (rightIndex !== undefined) {
        return 1
      }

      return left.industry.localeCompare(right.industry)
    })
}

function readStoredFolderOrder() {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(supplierFolderOrderStorageKey) ?? '[]')
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : []
  } catch {
    return []
  }
}

function writeStoredFolderOrder(order: string[]) {
  window.localStorage.setItem(supplierFolderOrderStorageKey, JSON.stringify(order))
}

function readResolvedOpenQuestions(supplierId: number) {
  try {
    const parsed = JSON.parse(window.localStorage.getItem(resolvedOpenQuestionsKey(supplierId)) ?? '[]')
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === 'string') : []
  } catch {
    return []
  }
}

function writeResolvedOpenQuestions(supplierId: number, questions: string[]) {
  window.localStorage.setItem(resolvedOpenQuestionsKey(supplierId), JSON.stringify(questions))
}

function resolvedOpenQuestionsKey(supplierId: number) {
  return `${resolvedOpenQuestionsStoragePrefix}:${supplierId}`
}

function normalizeQuestionKey(value: string) {
  return value.trim().toLowerCase().replace(/\s+/g, ' ')
}

function reorderIndustryFolders(
  visibleIndustries: string[],
  currentOrder: string[],
  sourceIndustry: string,
  targetIndustry: string,
) {
  const visibleSet = new Set(visibleIndustries)
  const orderedVisible = [
    ...currentOrder.filter((industry) => visibleSet.has(industry)),
    ...visibleIndustries.filter((industry) => !currentOrder.includes(industry)).sort((left, right) => left.localeCompare(right)),
  ].filter((industry, index, order) => order.indexOf(industry) === index)
  const withoutSource = orderedVisible.filter((industry) => industry !== sourceIndustry)
  const targetIndex = withoutSource.indexOf(targetIndustry)

  if (targetIndex === -1) {
    return orderedVisible
  }

  return [
    ...withoutSource.slice(0, targetIndex),
    sourceIndustry,
    ...withoutSource.slice(targetIndex),
  ]
}

function buildSupplierIdentityKey(item: SupplierSummary) {
  return [
    normalizeIdentityPart(item.name),
    item.countryCode.trim().toUpperCase(),
    normalizeIdentityPart(item.industry),
  ].join('|')
}

function normalizeIdentityPart(value: string) {
  return value.trim().toLowerCase().replace(/\s+/g, ' ')
}

function isDevelopmentSupplier(item: SupplierSummary) {
  const name = item.name.toLowerCase()
  const website = item.websiteUrl?.toLowerCase() ?? ''

  return name.includes('smoke') ||
    name.includes('debug') ||
    name.includes('demo') ||
    name.includes('prompt') ||
    name.includes('learning') ||
    name.includes('test') ||
    website.includes('127.0.0.1') ||
    website.includes('example.com') ||
    website.includes('learning-demo')
}

function buildVisibleMatchCandidates(candidates: SupplierMatchCandidate[]) {
  const visible = new Map<string, SupplierMatchCandidate>()

  for (const candidate of candidates) {
    if (candidate.status === 'Rejected') {
      continue
    }

    const key = [
      normalizeIdentityPart(formatCandidateDisplayName(candidate.candidateName)),
      candidate.countryCode?.trim().toUpperCase() ?? '',
    ].join('|')
    const existing = visible.get(key)

    if (!existing || compareMatchCandidates(candidate, existing) < 0) {
      visible.set(key, candidate)
    }
  }

  return [...visible.values()].sort(compareMatchCandidates)
}

function buildKnownInformation({
  supplier,
  confirmedIdentity,
  latestAssessment,
  latestEvidenceQuality,
  reachableSourceCount,
}: {
  supplier: SupplierDetail
  confirmedIdentity: SupplierMatchCandidate | null
  latestAssessment: RiskAssessment | undefined
  latestEvidenceQuality: EvidenceQuality | null
  reachableSourceCount: number
}) {
  const items = [
    `Supplier record: ${supplier.name} in ${supplier.countryCode} for ${supplier.industry}`,
  ]

  if (supplier.websiteUrl || confirmedIdentity?.websiteUrl) {
    items.push(`Website available: ${readHostname(supplier.websiteUrl ?? confirmedIdentity?.websiteUrl ?? null)}`)
  }

  if (latestEvidenceQuality?.hasReachableRegistrySource) {
    items.push('Company-information source found in public research')
  } else {
    items.push('Company-information source still needs confirmation')
  }

  items.push(
    reachableSourceCount > 0
      ? `${reachableSourceCount} reachable public source${reachableSourceCount === 1 ? '' : 's'}`
      : 'No reachable public evidence source yet',
  )

  if (latestAssessment) {
    items.push('Research memo available in Open questions')
  }

  return items
}

function buildBriefingFacts(
  supplier: SupplierDetail,
  knownSupplierFacts: string[],
  latestEvidenceSnapshot: EvidenceSnapshot | null,
) {
  const items = [
    `Supplier: ${supplier.name}`,
    `Market context: ${supplier.countryCode} · ${supplier.industry}`,
  ]

  if (supplier.websiteUrl) {
    items.push(`Website: ${readHostname(supplier.websiteUrl)}`)
  }

  items.push(...knownSupplierFacts.slice(0, 6))

  if (latestEvidenceSnapshot?.companySummary.description) {
    items.push(`Summary: ${shortenText(latestEvidenceSnapshot.companySummary.description, 260)}`)
  }

  return distinctText(items).slice(0, 10)
}

function buildOpenQuestionItems({
  displayMissingInformation,
  latestAssessment,
  latestEvidenceSnapshot,
  nextEvidenceItems,
}: {
  displayMissingInformation: string[]
  latestAssessment: RiskAssessment | undefined
  latestEvidenceSnapshot: EvidenceSnapshot | null
  nextEvidenceItems: string[]
}) {
  const items = [
    ...displayMissingInformation,
    ...nextEvidenceItems,
  ]

  if (!latestAssessment) {
    items.push('No research memo has been generated yet.')
  }

  if (latestEvidenceSnapshot?.supplierProfile.isSparseInput) {
    items.push('The public profile is still limited, so the briefing should be treated as incomplete.')
  }

  return distinctText(items).slice(0, 8)
}

function buildKnownSupplierFacts(facts: SupplierFact[]) {
  return distinctText(safeArray(facts)
    .filter((fact) => fact.factType !== 'MissingEvidence' && fact.factType !== 'SourceLimitation')
    .map((fact) => `${formatFactType(fact.factType)}: ${shortenText(fact.value, 260)}`))
    .slice(0, 8)
}

function buildBriefCompanySummary(
  supplier: SupplierDetail,
  latestEvidenceSnapshot: EvidenceSnapshot | null,
) {
  const candidates = [
    latestEvidenceSnapshot?.companySummary.description,
    ...buildFactSectionItems(supplier.supplierFacts, ['CompanyDescription', 'IndustryProfile']),
  ].filter((item): item is string => Boolean(item && item.trim()))

  return candidates
    .map((item) => formatBriefingSentence(item, 240))
    .find(Boolean) ?? null
}

function buildFactSectionItems(facts: SupplierFact[], factTypes: string[]) {
  const allowedTypes = new Set(factTypes)

  return distinctText(safeArray(facts)
    .filter((fact) => allowedTypes.has(fact.factType))
    .sort(compareSupplierFacts)
    .map((fact) => formatBriefingBullet(fact.value)))
    .filter(Boolean)
    .slice(0, 6)
}

function buildLocationCluesFromSources(sourceChecks: SourceCheck[]) {
  const locationTerms = [
    'address',
    'headquartered',
    'headquarters',
    'located',
    'location',
    'registered',
    'market',
    'markets',
    'city',
    'province',
    'district',
    'country',
    'china',
    'germany',
    'usa',
    'europe',
    'global',
  ]

  return distinctText(sourceChecks
    .flatMap((sourceCheck) => splitIntoSentences(sourceCheck.notes))
    .filter((sentence) => locationTerms.some((term) => sentence.toLowerCase().includes(term)))
    .map((sentence) => formatBriefingBullet(sentence)))
    .slice(0, 4)
}

function formatBriefingSentence(value: string, maxLength: number) {
  const cleaned = cleanBriefingText(value)
  const usefulSentences = splitIntoSentences(cleaned)
    .filter((sentence) => !looksLikeMetaSentence(sentence))
    .slice(0, 2)
  const sentence = usefulSentences.length > 0 ? usefulSentences.join(' ') : cleaned

  return shortenText(sentence, maxLength)
}

function formatBriefingBullet(value: string) {
  const cleaned = cleanBriefingText(value)
  const usefulSentence = splitIntoSentences(cleaned)
    .find((sentence) => !looksLikeMetaSentence(sentence)) ?? cleaned

  return shortenText(usefulSentence, 180)
}

function cleanBriefingText(value: string) {
  return value
    .replace(/\*\*/g, '')
    .replace(/^\s*[-•]\s*/, '')
    .replace(/\bReached source with HTTP 200\.\s*/gi, '')
    .replace(/\bSource URLs?:.*$/gi, '')
    .replace(/\s+/g, ' ')
    .trim()
}

function splitIntoSentences(value: string) {
  return value
    .split(/(?<=[.!?])\s+|;\s+|\s+-\s+/)
    .map((sentence) => sentence.trim())
    .filter((sentence) => sentence.length >= 20)
}

function looksLikeMetaSentence(value: string) {
  const lower = value.toLowerCase()

  return lower.includes('no source urls') ||
    lower.includes('source urls') ||
    lower.includes('evidence quality') ||
    lower.includes('recommended next checks') ||
    lower.includes('internal calculation')
}

function compareSupplierFacts(left: SupplierFact, right: SupplierFact) {
  return factConfidenceRank(right.confidence) - factConfidenceRank(left.confidence) ||
    Date.parse(right.createdAt) - Date.parse(left.createdAt)
}

function factConfidenceRank(value: string) {
  if (value === 'High') {
    return 3
  }

  if (value === 'Medium') {
    return 2
  }

  return 1
}

function buildBriefingSourcesUsed(sourceChecks: SourceCheck[]) {
  return [...sourceChecks]
    .sort(compareSourceChecks)
    .slice(0, 8)
    .map((sourceCheck) => {
      const host = readHostname(sourceCheck.url) ?? sourceCheck.url
      return `${sourceCheck.sourceName} (${formatSourceStatus(sourceCheck.status)}): ${host}`
    })
}

function connectionStrengthClass(value: string) {
  if (value.toLowerCase().includes('strong')) {
    return 'strong'
  }

  if (value.toLowerCase().includes('useful')) {
    return 'useful'
  }

  return 'light'
}

function connectionShortReason(connection: SupplierConnection) {
  const sharedTermReason = connection.reasons.find((reason) => reason.startsWith('Shared research terms'))
  const sharedHostReason = connection.reasons.find((reason) => reason.startsWith('Shared source host'))
  const reason = sharedTermReason ?? sharedHostReason ?? connection.reasons[0] ?? connection.strengthLabel

  return shortenText(reason.replace('Shared research terms: ', '').replace('Shared source host: ', ''), 34)
}

function buildUsefulResearchSources(sources: ResearchSource[]) {
  return distinctText(safeArray(sources)
    .filter((source) => source.relevance !== 'Low' || source.status !== 'Reachable')
    .map((source) => `${source.sourceName} (${source.status}, ${source.relevance}): ${shortenText(source.summary, 220)}`))
    .slice(0, 6)
}

function distinctText(items: string[]) {
  const seen = new Set<string>()
  return items.filter((item) => {
    const key = item.trim().toLowerCase()
    if (seen.has(key)) {
      return false
    }

    seen.add(key)
    return true
  })
}

function formatFactType(value: string) {
  return value
    .replace(/([a-z])([A-Z])/g, '$1 $2')
    .replace(/^./, (character) => character.toUpperCase())
}

function shortenText(value: string, maxLength: number) {
  const normalized = value.replace(/\s+/g, ' ').trim()
  return normalized.length <= maxLength ? normalized : `${normalized.slice(0, maxLength - 3).trim()}...`
}

type EvidenceQuality = {
  score: number
  band: string
  hasVerifiedCertification: boolean
  hasWebsiteCertificationClaim: boolean
  hasReachableWebsite: boolean
  hasReachableRegistrySource: boolean
  hasReachableVatSource: boolean
  hasBlockedOrFailedSource: boolean
}

type EvidenceSnapshot = {
  supplierProfile: {
    inputDepth: number
    isSparseInput: boolean
  }
  riskDecision: {
    reason: string
  }
  companySummary: {
    description: string
    ownWebsiteHighlights: string[]
    externalHighlights: string[]
  }
  automationFindings: string[]
  expectedEvidence: string[]
  recommendedNextChecks: string[]
}

function readEvidenceQuality(snapshotJson: string | null): EvidenceQuality | null {
  if (!snapshotJson) {
    return null
  }

  try {
    const parsed = JSON.parse(snapshotJson) as { evidenceQuality?: EvidenceQuality }
    return parsed.evidenceQuality ?? null
  } catch {
    return null
  }
}

function readEvidenceSnapshot(snapshotJson: string | null): EvidenceSnapshot | null {
  if (!snapshotJson) {
    return null
  }

  try {
    const parsed = JSON.parse(snapshotJson) as Partial<EvidenceSnapshot>

    if (!parsed.supplierProfile || !parsed.riskDecision) {
      return null
    }

    return {
      supplierProfile: {
        inputDepth: parsed.supplierProfile.inputDepth ?? 0,
        isSparseInput: parsed.supplierProfile.isSparseInput ?? false,
      },
      riskDecision: {
        reason: parsed.riskDecision.reason ?? 'No decision reason stored.',
      },
      companySummary: {
        description:
          parsed.companySummary?.description ??
          'No company description found in the available evidence yet.',
        ownWebsiteHighlights: parsed.companySummary?.ownWebsiteHighlights ?? [],
        externalHighlights: parsed.companySummary?.externalHighlights ?? [],
      },
      automationFindings: parsed.automationFindings ?? [],
      expectedEvidence: parsed.expectedEvidence ?? [],
      recommendedNextChecks: parsed.recommendedNextChecks ?? [],
    }
  } catch {
    return null
  }
}

function formatBoolean(value: boolean) {
  return value ? 'Yes' : 'No'
}

function normalizeSourceStatus(value: string): SourceCheckInput['status'] {
  return value === 'Reachable' || value === 'Blocked' || value === 'Failed' || value === 'NotChecked'
    ? value
    : 'NotChecked'
}

function formatShortDate(value: string) {
  const timestamp = Date.parse(value)

  if (Number.isNaN(timestamp)) {
    return value
  }

  return new Intl.DateTimeFormat(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  }).format(new Date(timestamp))
}

function formatRiskAssessmentScore(assessment: RiskAssessment | undefined) {
  if (!assessment) {
    return 'No score yet'
  }

  if (assessment.riskLevel === 'Unknown') {
    return 'Not scored'
  }

  return `${assessment.score}/100`
}

function buildRuntimeKeyMessage(action: 'save' | 'test', status: LocalModelStatus) {
  if (!status.isApiKeyConfigured) {
    return {
      tone: 'error' as const,
      text: 'No OpenRouter key is configured. Paste a key and save it for this run.',
    }
  }

  if (status.isReachable) {
    const keyLabel = status.apiKeyFingerprint ? ` using ${status.apiKeyFingerprint}` : ''

    return {
      tone: 'ok' as const,
      text: action === 'save'
        ? `OpenRouter key saved for this run${keyLabel}. Connection works (${status.models.length} models found).`
        : `OpenRouter connection works${keyLabel} (${status.models.length} models found).`,
    }
  }

  return {
    tone: 'error' as const,
    text: status.errorMessage ?? 'OpenRouter key is configured, but the connection test failed.',
  }
}

function buildNextAction({
  hasConfirmedIdentity,
  loadingMatchCandidates,
  matchCandidateCount,
  nextEvidenceCount,
  latestAssessment,
}: {
  hasConfirmedIdentity: boolean
  loadingMatchCandidates: boolean
  matchCandidateCount: number
  nextEvidenceCount: number
  latestAssessment: RiskAssessment | undefined
}): {
  title: string
  description: string
  buttonLabel: string
  status: string
  step: ReviewStep
} {
  if (nextEvidenceCount > 0) {
    return {
      title: 'Review source gaps',
      description: 'Useful research may be available, but some source or fact gaps still need attention.',
      buttonLabel: 'Review sources',
      status: `${nextEvidenceCount} open gaps`,
      step: 'sources',
    }
  }

  if (!latestAssessment) {
    return {
      title: 'Review open questions',
      description: 'Sources are available, but no research memo has been stored for unresolved questions yet.',
      buttonLabel: 'Open questions',
      status: 'Questions pending',
      step: 'questions',
    }
  }

  return {
    title: 'Research briefing ready',
    description: 'The main public research inputs are available. Review similar suppliers and recommendations.',
    buttonLabel: 'Open connections',
    status: 'Ready',
    step: 'connections',
  }
}

function summarizeMatchCandidates(candidates: SupplierMatchCandidate[]) {
  const confirmed = candidates.filter((candidate) => candidate.status === 'Confirmed').length
  const proposed = candidates.filter((candidate) => candidate.status === 'Proposed').length

  if (confirmed > 0) {
    return `${confirmed} confirmed`
  }

  if (proposed > 0) {
    return `${proposed} proposed`
  }

  return `${candidates.length} candidates`
}

function compareMatchCandidates(a: SupplierMatchCandidate, b: SupplierMatchCandidate) {
  return matchStatusRank(b.status) - matchStatusRank(a.status) ||
    b.confidenceScore - a.confidenceScore ||
    Date.parse(b.createdAt) - Date.parse(a.createdAt)
}

function matchStatusRank(status: SupplierMatchCandidate['status']) {
  if (status === 'Confirmed') {
    return 3
  }

  if (status === 'Proposed') {
    return 2
  }

  return 1
}

function formatCandidateLocation(candidate: SupplierMatchCandidate) {
  return [candidate.countryCode, readHostname(candidate.websiteUrl)]
    .filter(Boolean)
    .join(' · ') || 'No location details'
}

function formatCandidateDisplayName(value: string) {
  const normalized = value
    .replace(/\*\*/g, '')
    .replace(/\s+/g, ' ')
    .trim()

  const companyNumberIndex = normalized.search(/\s+\(company\s+number/i)
  if (companyNumberIndex > 0) {
    return normalized.slice(0, companyNumberIndex).trim()
  }

  const sentenceMarker = normalized.search(/\s+(is|operates|appears|runs|speciali[sz]es)\s+/i)
  if (sentenceMarker > 0 && sentenceMarker <= 80) {
    return normalized.slice(0, sentenceMarker).trim()
  }

  return shortenText(normalized, 90)
}

function readHostname(url: string | null) {
  if (!url) {
    return null
  }

  try {
    return new URL(url).hostname
  } catch {
    return url
  }
}

function formatMatchConfidence(score: number) {
  if (score >= 75) {
    return 'Strong match'
  }

  if (score >= 50) {
    return 'Plausible match'
  }

  return 'Weak match'
}

function formatCandidateStatus(status: SupplierMatchCandidate['status']) {
  if (status === 'Confirmed') {
    return 'Saved'
  }

  if (status === 'Rejected') {
    return 'Dismissed'
  }

  return 'Needs decision'
}

function SnapshotList({ title, items }: { title: string; items: string[] }) {
  if (items.length === 0) {
    return null
  }

  return (
    <div className="snapshot-list">
      <strong>{title}</strong>
      <ul>
        {items.map((item, index) => (
          <li key={`${item}-${index}`}>{item}</li>
        ))}
      </ul>
    </div>
  )
}

function AnalysisJobList({ jobs }: { jobs: AnalysisJob[] }) {
  if (jobs.length === 0) {
    return <p className="muted evidence-empty">No analysis jobs stored.</p>
  }

  const sortedJobs = [...jobs].sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
  const currentJob = sortedJobs.find(isActiveAnalysisJob) ?? sortedJobs[0]
  const historicalJobs = sortedJobs.filter((job) => job.id !== currentJob.id)

  return (
    <div className="analysis-job-list">
      <AnalysisJobCard job={currentJob} label={isActiveAnalysisJob(currentJob) ? 'Current run' : 'Latest run'} />
      {historicalJobs.length > 0 && (
        <details className="analysis-job-history">
          <summary>{historicalJobs.length} previous run{historicalJobs.length === 1 ? '' : 's'}</summary>
          {historicalJobs.map((job) => (
            <AnalysisJobCard job={job} key={job.id} label="History" />
        ))}
        </details>
      )}
    </div>
  )
}

function AnalysisJobCard({ job, label }: { job: AnalysisJob; label: string }) {
  return (
    <article className={`analysis-job analysis-${job.status.toLowerCase()}`} key={job.id}>
      <div>
        <strong>{label}: {job.status}</strong>
        <span>{job.jobType}</span>
      </div>
      <p>{job.progressMessage}</p>
      {job.errorMessage && <p className="analysis-job-error">{job.errorMessage}</p>}
    </article>
  )
}

function buildNextEvidenceItems(supplier: SupplierDetail, reachableSourceCount: number) {
  const items: string[] = []

  if (!supplier.sourceChecks.some((sourceCheck) => sourceCheck.sourceName.toLowerCase().includes('registry'))) {
    items.push('Company-information source still needs confirmation.')
  }

  if (reachableSourceCount === 0) {
    items.push('No public source confirmed.')
  }

  if (supplier.sourceChecks.some((sourceCheck) => sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed')) {
    items.push('Some sources could not be verified.')
  }

  return items
}

function CompactSourceCheckList({
  deletingSourceId,
  sourceChecks,
  onDelete,
  onEdit,
}: {
  deletingSourceId: number | null
  sourceChecks: SourceCheck[]
  onDelete: (sourceCheckId: number) => void
  onEdit: (sourceCheck: SourceCheck) => void
}) {
  if (sourceChecks.length === 0) {
    return <p className="muted evidence-empty">No public sources verified yet.</p>
  }

  return (
    <div className="compact-list">
      {[...sourceChecks]
        .sort(compareSourceChecks)
        .map((sourceCheck) => {
          const quality = scoreSourceCheck(sourceCheck)

          return (
            <article className="evidence-item" key={sourceCheck.id}>
              <div className="evidence-title-row">
                <strong>{sourceCheck.sourceName}</strong>
                <div className="source-card-controls">
                  <span className={`source-quality source-quality-${quality.level.toLowerCase()}`}>
                    {quality.level}
                  </span>
                  <button
                    aria-label={`Edit ${sourceCheck.sourceName}`}
                    className="icon-action"
                    type="button"
                    onClick={() => onEdit(sourceCheck)}
                  >
                    ✏️
                  </button>
                  <button
                    aria-label={`Delete ${sourceCheck.sourceName}`}
                    className="icon-action danger"
                    disabled={deletingSourceId === sourceCheck.id}
                    type="button"
                    onClick={() => onDelete(sourceCheck.id)}
                  >
                    -
                  </button>
                </div>
              </div>
              <div className="source-meta-row">
                <span>{formatSourceStatus(sourceCheck.status)}</span>
                <span>{formatSourceKind(sourceCheck)}</span>
              </div>
              <a href={sourceCheck.url} rel="noreferrer" target="_blank">
                {sourceCheck.url}
              </a>
              <p>{shortenText(sourceCheck.notes || 'No notes', 420)}</p>
              <div className="source-card-actions">
                <a className="source-open-link" href={sourceCheck.url} rel="noreferrer" target="_blank">
                  Open source
                </a>
              </div>
            </article>
          )
        })}
    </div>
  )
}

function compareSourceChecks(a: SourceCheck, b: SourceCheck) {
  return scoreSourceCheck(b).score - scoreSourceCheck(a).score ||
    Date.parse(b.checkedAt) - Date.parse(a.checkedAt)
}

function scoreSourceCheck(sourceCheck: SourceCheck) {
  let score = 15
  const name = sourceCheck.sourceName.toLowerCase()
  const notes = sourceCheck.notes.toLowerCase()
  const url = sourceCheck.url.toLowerCase()

  if (sourceCheck.status === 'Reachable') {
    score += 25
  }

  if (name.includes('supplier website') || name.includes('website research')) {
    score += 18
  }

  if (name.includes('ai web search')) {
    score += url.includes('openrouter.ai') ? 10 : 18
  }

  if (name.includes('registry') || name.includes('vat') || name.includes('vies')) {
    score += 22
  }

  if (notes.includes('source urls:') || notes.includes('http')) {
    score += 10
  }

  if (notes.includes('certificate') || notes.includes('iso ') || notes.includes('iatf') || notes.includes('as9100')) {
    score += 8
  }

  if (sourceCheck.status === 'Blocked' || sourceCheck.status === 'Failed') {
    score -= 25
  }

  if (url.includes('openrouter.ai/search')) {
    score -= 12
  }

  const boundedScore = Math.max(0, Math.min(100, score))
  const level = boundedScore >= 70 ? 'High' : boundedScore >= 40 ? 'Medium' : 'Low'

  return { level, score: boundedScore }
}

function formatSourceKind(sourceCheck: SourceCheck) {
  const sourceName = sourceCheck.sourceName.toLowerCase()
  const url = sourceCheck.url.toLowerCase()

  if (sourceName.includes('registry') || sourceName.includes('vat') || sourceName.includes('vies')) {
    return 'registry'
  }

  if (sourceName.includes('supplier website') || sourceName.includes('website research')) {
    return 'own website'
  }

  if (sourceName.includes('ai web search')) {
    return url.includes('openrouter.ai') ? 'search summary' : 'external source'
  }

  if (sourceName.includes('cert')) {
    return 'claim source'
  }

  return 'public source'
}

function readError(exception: unknown) {
  return exception instanceof Error ? exception.message : 'Unexpected error'
}

function formatSourceStatus(status: string) {
  if (status === 'Reachable') {
    return 'Available'
  }

  if (status === 'Blocked') {
    return 'Could not access'
  }

  if (status === 'Failed') {
    return 'Verification failed'
  }

  return status
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
)
