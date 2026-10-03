import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'
import { ApiError, SessionExpiredError } from './api'
import { accessQueryKey, sessionQueryKey } from './session'

/**
 * Herhangi bir istek 401 alırsa oturum yeniden soruluyor; BFF oturumu kapattıysa
 * (Keycloak'taki oturum bitti) uygulama girişe dönüyor. 403 alırsa çalışanın izinleri
 * yeniden soruluyor: rolü az önce alındıysa menü ve düğmeler de değişiyor.
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

        if (query.queryKey[0] !== accessQueryKey[0]) {
          refreshAccess(error)
        }
      },
    }),
    mutationCache: new MutationCache({
      onError: (error) => {
        expireSession(error)
        refreshAccess(error)
      },
    }),
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

  function refreshAccess(error: Error) {
    if (error instanceof ApiError && error.status === 403) {
      void client.invalidateQueries({ queryKey: accessQueryKey })
    }
  }

  return client
}

function isTransient(error: Error): boolean {
  return !(error instanceof SessionExpiredError) && !(error instanceof ApiError && error.status < 500)
}
