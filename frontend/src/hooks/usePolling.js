import { useCallback, useEffect, useRef, useState } from 'react';
import { getErrorMessage } from '../api/client';

// load must be memoized. Refresh aborts any previous read, including the polling request.
export default function usePolling(load, enabled = true) {
  const [snapshot, setSnapshot] = useState(null);
  const refreshRef = useRef(() => Promise.resolve());
  useEffect(() => {
    let active = true;
    let timer;
    let controller;
    const refresh = async () => {
      clearTimeout(timer);
      controller?.abort();
      const request = new AbortController();
      controller = request;
      try {
        const result = await load(request.signal);
        if (active && !request.signal.aborted) {
          setSnapshot({ load, data: result, error: '' });
        }
      } catch (err) {
        if (active && !request.signal.aborted) setSnapshot(previous => ({
          load, data: previous?.load === load ? previous.data : null, error: getErrorMessage(err),
        }));
      } finally {
        if (active && !request.signal.aborted) {
          if (enabled) timer = setTimeout(refresh, 3000);
        }
      }
    };
    refreshRef.current = refresh;
    refresh();
    return () => {
      active = false;
      clearTimeout(timer);
      controller?.abort();
      refreshRef.current = () => Promise.resolve();
    };
  }, [load, enabled]);
  const refresh = useCallback(() => refreshRef.current(), []);
  const current = snapshot?.load === load ? snapshot : null;
  return { data: current?.data ?? null, error: current?.error ?? '', loading: !current, refresh };
}
