import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ModelSettings } from './ModelSettings'
import { renderWithLang } from '../test-utils'
import type { ProvidersResponse } from '../api/client'

const providers = vi.fn()
const saveProvider = vi.fn()
const forgetProvider = vi.fn()
const setNarratorProvider = vi.fn()

vi.mock('../api/client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('../api/client')>()),
  api: {
    providers: () => providers(),
    saveProvider: (...a: unknown[]) => saveProvider(...a),
    forgetProvider: (...a: unknown[]) => forgetProvider(...a),
    setNarratorProvider: (...a: unknown[]) => setNarratorProvider(...a),
  },
}))

const response: ProvidersResponse = {
  active: 'gemini',
  options: [
    { provider: 'gemini', model: 'gemini-3.6-flash', ready: true, reason: null },
    { provider: 'openai-compatible', model: 'gpt-4o-mini', ready: false, reason: 'no endpoint' },
  ],
  providers: [
    {
      provider: 'gemini',
      hasKey: true,
      keyHint: 'XO3w',
      baseUrl: null,
      model: null,
      keyUnreadable: false,
      configuredFromEnvironment: true,
      updatedAt: null,
      updatedBy: null,
    },
    {
      provider: 'openai-compatible',
      hasKey: false,
      keyHint: null,
      baseUrl: null,
      model: null,
      keyUnreadable: false,
      configuredFromEnvironment: false,
      updatedAt: null,
      updatedBy: null,
    },
  ],
}

beforeEach(() => {
  providers.mockReset().mockResolvedValue(response)
  saveProvider.mockReset().mockResolvedValue({ provider: 'gemini', keyClearedByEndpointChange: false })
  forgetProvider.mockReset().mockResolvedValue(undefined)
  setNarratorProvider.mockReset().mockResolvedValue({ active: 'gemini', model: 'gemini-3.6-flash' })
})

describe('ModelSettings', () => {
  it('shows a hint instead of the key, because the key cannot be fetched', async () => {
    renderWithLang(<ModelSettings reviewer="Niko" />)

    expect(await screen.findByText(/····XO3w/)).toBeInTheDocument()

    // The key input starts empty and is a password field — there is nothing to prefill it with.
    const inputs = document.querySelectorAll('input[type="password"]')
    expect(inputs.length).toBeGreaterThan(0)
    inputs.forEach((input) => expect((input as HTMLInputElement).value).toBe(''))
  })

  it('will not let you select a provider that cannot run', async () => {
    renderWithLang(<ModelSettings reviewer="Niko" />)

    await screen.findByText(/gpt-4o-mini/)
    const radios = screen.getAllByRole('radio')

    expect(radios[0]).toBeChecked()
    expect(radios[1]).toBeDisabled()
    expect(screen.getByText(/no endpoint/)).toBeInTheDocument()
  })

  it('omits the key when the field is left blank, so a stored one survives an edit', async () => {
    const user = userEvent.setup()
    renderWithLang(<ModelSettings reviewer="Niko" />)

    await screen.findByText(/····XO3w/)
    await user.click(screen.getAllByRole('button', { name: 'Save' })[0])

    await waitFor(() => expect(saveProvider).toHaveBeenCalled())
    const [, body] = saveProvider.mock.calls[0]

    // Omitted entirely — an empty string would mean "delete the stored key".
    expect(body).not.toHaveProperty('apiKey')
    expect(body.updatedBy).toBe('Niko')
  })

  it('sends a key that was typed', async () => {
    const user = userEvent.setup()
    renderWithLang(<ModelSettings reviewer="Niko" />)

    await screen.findByText(/····XO3w/)
    await user.type(document.querySelectorAll('input[type="password"]')[0] as HTMLInputElement, 'sk-FAKE-1234')
    await user.click(screen.getAllByRole('button', { name: 'Save' })[0])

    await waitFor(() => expect(saveProvider).toHaveBeenCalled())
    expect(saveProvider.mock.calls[0][1].apiKey).toBe('sk-FAKE-1234')
  })

  it('says so when the endpoint change cleared the key', async () => {
    const user = userEvent.setup()
    saveProvider.mockResolvedValue({ provider: 'openai-compatible', keyClearedByEndpointChange: true })

    renderWithLang(<ModelSettings reviewer="Niko" />)
    await screen.findByText(/····XO3w/)

    await user.click(screen.getAllByRole('button', { name: 'Save' })[1])

    expect(await screen.findByText(/key was cleared because the endpoint changed/)).toBeInTheDocument()
  })

  it('warns before saving that moving the endpoint will clear the key', async () => {
    const user = userEvent.setup()
    providers.mockResolvedValue({
      ...response,
      providers: [
        response.providers[0],
        { ...response.providers[1], hasKey: true, keyHint: '9999', baseUrl: 'http://localhost:11434/v1' },
      ],
    })

    renderWithLang(<ModelSettings reviewer="Niko" />)
    const endpoint = await screen.findByDisplayValue('http://localhost:11434/v1')

    await user.clear(endpoint)
    await user.type(endpoint, 'http://elsewhere.example/v1')

    expect(screen.getByText(/Changing the endpoint clears the stored key/)).toBeInTheDocument()
  })

  it('reports a key that could not be decrypted', async () => {
    providers.mockResolvedValue({
      ...response,
      providers: [{ ...response.providers[0], keyUnreadable: true }, response.providers[1]],
    })

    renderWithLang(<ModelSettings reviewer="Niko" />)

    expect(await screen.findByText(/could not be decrypted/)).toBeInTheDocument()
  })

  it('states the trade-off of storing secrets without a sign-in', async () => {
    renderWithLang(<ModelSettings reviewer="Niko" />)

    expect(await screen.findByText(/There is no sign-in yet/)).toBeInTheDocument()
  })
})
