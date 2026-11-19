import client from './client';

export const healthApi = {
  check: signal =>
    client.get('/health', { signal })
      .then(res => res.data)
      .catch(err => {
        if (err.response?.status === 503 && err.response?.data) {
          return err.response.data;
        }
        throw err;
      }),
};
