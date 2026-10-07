import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError, SessionExpiredError } from './api'
import { sessionQueryKey } from './session'

/**
 * Herhangi bir istek 401 alırsa oturum yeniden soruluyor; BFF oturumu kapattıysa
 * (Keycloak'taki oturum bitti) uygulama girişe dönüyor.
 *
 * Okumalar yalnızca geçici hatada (ağ, 5xx) yeniden deneniyor. Para hareketi başlatan
 * istekler HİÇ yeniden denenmiyor: tekrar kullanıcının kararı ve aynı anahtarla.
 */
export function createQueryClient(): QueryClient {
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({
      onError: (error, query) => {
        if (query.queryKey[0] !== sessionQueryKey[0]) {
          expireSession(error)
        }
      },
    }),
    mutationCache: new MutationCache({ onError: (error) => expireSession(error) }),
    defaultOptions: {
      queries: { retry: (failureCount, error) => isTransient(error) && failureCount < 2 },
      mutations: { retry: false },
    },
  })

  function expireSession(error: Error) {
    if (error instanceof SessionExpiredError) {
      void client.resetQueries({ queryKey: sessionQueryKey })
    }
  }

  return client
}

function isTransient(error: Error): boolean {
  return !(error instanceof SessionExpiredError) && !(error instanceof ApiError && error.status < 500)
}
