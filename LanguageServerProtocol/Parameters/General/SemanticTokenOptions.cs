using LanguageServer.Json;

namespace LanguageServerProtocol.Parameters.General;

public class SemanticTokensOptions
{

    /**
	 * The legend used by the server
	 */
    public SemanticTokensLegend legend = new();

    /**
	 * Server supports providing semantic tokens for a specific range
	 * of a document.
	 */
    public bool? range { get; set; }

    /**
	 * Server supports providing semantic tokens for a full document.
	 */
    public FullRequestCapabilitiesOrBoolean? full { get; set; }
}

public class SemanticTokensLegend
{
    public List<string> tokenTypes { get; set; } = new();
    public List<string> tokenModifiers { get; set; } = new();
}

public class FullRequestCapabilities
{
    /*
	* The server supports deltas for full documents.
	*/
    public bool? delta { get; set; }
}


public class FullRequestCapabilitiesOrBoolean : Either
{
    /// <summary>
    /// Defines an implicit conversion of a <see cref="FullRequestCapabilities"/> to a <see cref="FullRequestCapabilitiesOrBoolean"/>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <seealso>Spec 3.8.0</seealso>
    public static implicit operator FullRequestCapabilitiesOrBoolean(FullRequestCapabilities value)
        => new FullRequestCapabilitiesOrBoolean(value);

    /// <summary>
    /// Defines an implicit conversion of a <see cref="bool"/> to a <see cref="FullRequestCapabilitiesOrBoolean"/>
    /// </summary>
    /// <param name="value"></param>
    /// <returns></returns>
    /// <seealso>Spec 3.8.0</seealso>
    public static implicit operator FullRequestCapabilitiesOrBoolean(bool value)
        => new FullRequestCapabilitiesOrBoolean(value);

    /// <summary>
    /// Initializes a new instance of <c>ColorProviderOptionsOrBoolean</c> with the specified value.
    /// </summary>
    /// <param name="value"></param>
    /// <seealso>Spec 3.8.0</seealso>
    public FullRequestCapabilitiesOrBoolean(FullRequestCapabilities value)
    {
        Type = typeof(FullRequestCapabilities);
        Value = value;
    }

    /// <summary>
    /// Initializes a new instance of <c>FullRequestCapabilitiesOrBoolean</c> with the specified value.
    /// </summary>
    /// <param name="value"></param>
    /// <seealso>Spec 3.8.0</seealso>
    public FullRequestCapabilitiesOrBoolean(bool value)
    {
        Type = typeof(bool);
        Value = value;
    }

    /// <summary>
    /// Returns true if its underlying value is a <see cref="FullRequestCapabilities"/>.
    /// </summary>
    /// <seealso>Spec 3.8.0</seealso>
    public bool IsFullRequestCapabilities => Type == typeof(FullRequestCapabilities);

    /// <summary>
    /// Returns true if its underlying value is a <see cref="bool"/>.
    /// </summary>
    /// <seealso>Spec 3.8.0</seealso>
    public bool IsBoolean => Type == typeof(bool);

    /// <summary>
    /// Gets the value of the current object if its underlying value is a <see cref="FullRequestCapabilities"/>.
    /// </summary>
    /// <seealso>Spec 3.8.0</seealso>
    public FullRequestCapabilities FullRequestCapabilities => GetValue<FullRequestCapabilities>();

    /// <summary>
    /// Gets the value of the current object if its underlying value is a <see cref="bool"/>.
    /// </summary>
    /// <seealso>Spec 3.8.0</seealso>
    public bool Boolean => GetValue<bool>();
}