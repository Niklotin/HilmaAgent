import { Component, type ReactNode } from 'react'

/**
 * Keeps one malformed assessment from blanking the whole queue.
 *
 * Worth having here specifically: assessments are historical records that are never rewritten, so
 * the UI will always be reading rows produced by older versions of the code. A card that cannot
 * render should say so and let the reviewer carry on with the rest.
 */
export class ErrorBoundary extends Component<{ children: ReactNode; label?: string }, { error: Error | null }> {
  state = { error: null as Error | null }

  static getDerivedStateFromError(error: Error) {
    return { error }
  }

  render() {
    if (this.state.error) {
      return (
        <article className="card">
          <p className="error">
            {this.props.label ?? 'This item'} could not be displayed: {this.state.error.message}
          </p>
          <p className="muted">The rest of the queue is unaffected.</p>
        </article>
      )
    }

    return this.props.children
  }
}
