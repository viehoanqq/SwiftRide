import client from './client';

export const availabilityApi = {
  getMe: signal =>
    client.get('/availability/me', { signal })
      .then(res => res.data)
      .catch(err => {
        if (err.response?.status === 404) return null;
        throw err;
      }),
  setOnline: (lat, lng) =>
    client.post('/availability/online', { lat: Number(lat), lng: Number(lng) }).then(res => res.data),
  setOffline: () =>
    client.post('/availability/offline').then(res => res.data),
};
