import { ApiError } from '../api'
import { errorText } from '../errors'

/** BFF'in reddi ProblemDetails; doğrulama hatalarında alan başına mesajlar da gösteriliyor. */
export function ErrorMessage({ error }: { error: Error }) {
  const fieldErrors = error instanceof ApiError ? Object.values(error.problem?.errors ?? {}).flat() : []

  return (
    <div className="error" role="alert">
      <p>{errorText(error)}</p>
      {fieldErrors.length > 0 && (
        <ul>
          {fieldErrors.map((message) => (
            <li key={message}>{message}</li>
          ))}
        </ul>
      )}
    </div>
  )
}
