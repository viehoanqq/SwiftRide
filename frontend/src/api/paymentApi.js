import client from './client';

export const paymentApi = {
  getById: (id, signal) =>
    client.get(`/payments/${id}`, { signal }).then(res => res.data),
  getByTripId: (tripId, signal) =>
    client.get(`/payments/by-trip/${tripId}`, { signal }).then(res => res.data),
};
