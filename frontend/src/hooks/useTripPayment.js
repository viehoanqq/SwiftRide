import { useCallback } from 'react';
import { paymentApi } from '../api/paymentApi';
import usePolling from './usePolling';

export default function useTripPayment(tripId, status) {
  const settled = status === 'Paid' || status === 'PaymentFailed';
  const load = useCallback(signal => settled
    ? paymentApi.getByTripId(tripId, signal)
    : Promise.resolve(null), [tripId, settled]);
  return usePolling(load, settled);
}
