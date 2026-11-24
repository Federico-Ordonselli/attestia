namespace Attestia.Core.Certificates;

/// <summary>Stato di un attestato rispetto a una data di riferimento.</summary>
public enum CertificateStatus
{
    Valid,
    ExpiringSoon,
    Expired,
    /// <summary>Corso obbligatorio per cui non esiste alcun attestato.</summary>
    Missing,
}
