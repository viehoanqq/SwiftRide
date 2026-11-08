import client from './client';

export const tripApi = {
  create: data => client.post('/trips', data).then(response => response.data),
  getById: id => client.get(`/trips/${id}`).then(response => response.data),
  getCurrent: () => client.get('/trips/me/current').then(response => (
    response.status === 204 ? null : response.data
  )),
  confirm: (id, accept, paymentMethod) => client.post(`/trips/${id}/confirm`, {
    accept,
    paymentMethod,
  }).then(response => response.data),
};
