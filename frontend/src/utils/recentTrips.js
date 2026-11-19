const KEY_PREFIX = 'swiftride_recent_trips_';
const MAX_ITEMS = 10;

export function getRecentTrips(userId) {
  if (!userId) return [];
  try {
    const raw = localStorage.getItem(`${KEY_PREFIX}${userId}`);
    const parsed = raw ? JSON.parse(raw) : [];
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    return [];
  }
}

export function rememberTrip(userId, trip) {
  if (!userId || !trip?.id) return;
  try {
    const existing = getRecentTrips(userId).filter(item => item.id !== trip.id);
    const entry = {
      id: trip.id,
      status: trip.status || '',
      pickupAddress: trip.pickup?.address || '',
      dropoffAddress: trip.dropoff?.address || '',
      updatedAt: new Date().toISOString(),
    };
    localStorage.setItem(
      `${KEY_PREFIX}${userId}`,
      JSON.stringify([entry, ...existing].slice(0, MAX_ITEMS)),
    );
  } catch {
    // Bỏ qua nếu trình duyệt chặn localStorage
  }
}
