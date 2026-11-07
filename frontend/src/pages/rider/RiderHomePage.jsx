import { Navigate } from 'react-router-dom';
import { checkTrip } from '../../auth/ProtectedRoute';
const trip = null; // Replace with actual trip data or state management logic

export default function RiderHomePage() {
    return <Navigate to={checkTrip(trip)} replace />;
}