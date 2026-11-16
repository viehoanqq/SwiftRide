import client from './client';

export const paymentApi = {
  getByTripId: (tripId, signal) =>
    client.get(`/payments/by-trip/${tripId}`, { signal }).then(res => res.data),
};